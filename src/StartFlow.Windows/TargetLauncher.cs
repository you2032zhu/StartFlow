using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using StartFlow.Core;

namespace StartFlow.Windows;

public sealed class TargetLauncher : ITargetLauncher
{
    public Task<LaunchOutcome> LaunchAsync(StartupItem item, CancellationToken cancellation) => Task.Run(() => Start(item, cancellation), cancellation);
    private static LaunchOutcome Start(StartupItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var validation = Validation.CheckItem(item);
        if (validation.Count > 0) return new(RunStatus.Failed, string.Join(", ", validation));
        if (Validation.MissingTarget(item)) return new(RunStatus.Failed, "MissingTarget", true);
        var detection = RunningState.NotRunning;
        if (item.DetectRunning && item.Kind is TargetKind.Application or TargetKind.StoreApp)
        {
            detection = RunningDetector.Check(item);
            if (detection == RunningState.Running) return new(RunStatus.Skipped, "AlreadyRunning");
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            if (item.Kind == TargetKind.StoreApp)
            {
                var activation = (IApplicationActivationManager)new ApplicationActivationManager();
                try
                {
                    var hr = activation.ActivateApplication(item.Target, item.Arguments, 0, out _);
                    Marshal.ThrowExceptionForHR(hr);
                    return new(RunStatus.Submitted, detection == RunningState.Unknown ? "StoreDetectionUnknown" : "StoreSubmitted");
                }
                finally { Marshal.FinalReleaseComObject(activation); }
            }
            var psi = CreateStartInfo(item);
            ct.ThrowIfCancellationRequested();
            var process = Process.Start(psi);
            if (item.Kind is TargetKind.Script or TargetKind.Command)
            {
                if (process == null) return new(RunStatus.Submitted, "ExitUnavailable");
                return new(RunStatus.Running, "ProcessCreated", Completion: ObserveExit(process));
            }
            process?.Dispose();
            return new(RunStatus.Submitted, detection == RunningState.Unknown ? "DetectionUnknown" : "RequestSubmitted");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new(RunStatus.Cancelled, "UacCancelled"); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or COMException or ArgumentException)
        { return new(RunStatus.Failed, ex.Message, true); }
    }
    private static async Task<LaunchOutcome> ObserveExit(Process process)
    {
        using (process)
        {
            try { await process.WaitForExitAsync(); return process.ExitCode == 0 ? new(RunStatus.Succeeded, "ExitCode: 0") : new(RunStatus.Failed, $"ExitCode: {process.ExitCode}", true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return new(RunStatus.Submitted, "ExitUnavailable: " + ex.Message); }
        }
    }
    public static ProcessStartInfo CreateStartInfo(StartupItem item)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        var psi = new ProcessStartInfo
        {
            FileName = target, Arguments = item.Arguments, UseShellExecute = true,
            WorkingDirectory = Environment.ExpandEnvironmentVariables(item.WorkingDirectory),
            WindowStyle = item.Window switch { WindowMode.Maximized => ProcessWindowStyle.Maximized, WindowMode.Minimized => ProcessWindowStyle.Minimized, _ => ProcessWindowStyle.Normal }
        };
        if (item.RunAsAdmin) psi.Verb = "runas";
        if (item.Kind == TargetKind.Application && !item.RunAsAdmin) psi.UseShellExecute = false;
        if (item.Kind == TargetKind.Website) { psi.FileName = item.Target.Trim(); psi.Arguments = ""; }
        if (item.Kind == TargetKind.Folder) { psi.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"); psi.ArgumentList.Add(target); psi.Arguments = ""; }
        if (item.Kind == TargetKind.Command || item.Kind == TargetKind.Script)
        {
            var powershell = item.Kind == TargetKind.Command ? item.Shell == CommandShell.PowerShell : Path.GetExtension(target).Equals(".ps1", StringComparison.OrdinalIgnoreCase);
            psi.FileName = powershell
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            psi.UseShellExecute = item.RunAsAdmin;
            if (powershell)
            {
                if (item.Kind == TargetKind.Command)
                    psi.Arguments = "-NoLogo -NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(item.Target));
                else psi.Arguments = "-NoLogo -NoProfile -File " + QuoteArgument(target) + (item.Arguments.Length > 0 ? " " + item.Arguments : "");
            }
            else psi.Arguments = item.Kind == TargetKind.Command ? "/d /s /c \"" + item.Target + "\"" : "/d /s /c \"\"" + target + "\" " + item.Arguments + "\"";
        }
        return psi;
    }
    public static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            result.Append('\\', slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] private class ApplicationActivationManager { }
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
        [PreserveSig] int ActivateForFile(IntPtr itemArray, string verb, out uint processId);
        [PreserveSig] int ActivateForProtocol(IntPtr itemArray, out uint processId);
    }
}
public enum RunningState { NotRunning, Running, Unknown }
public static class RunningDetector
{
    public static RunningState Check(StartupItem item)
    {
        if (item.Kind is not (TargetKind.Application or TargetKind.StoreApp)) return RunningState.NotRunning;
        // Parameters can represent distinct work. Only opt into explicit process identity for these items.
        if (item.Arguments.Length > 0 && item.ProcessPath.Length == 0 && item.ProcessName.Length == 0) return RunningState.Unknown;
        var expectedPath = Validation.NormalizePath(item.ProcessPath.Length > 0 ? item.ProcessPath : item.Target);
        var name = item.ProcessName.Length > 0 ? Path.GetFileNameWithoutExtension(item.ProcessName) : Path.GetFileNameWithoutExtension(item.Target);
        var unknown = false;
        foreach (var process in item.Kind == TargetKind.StoreApp ? Process.GetProcesses() : Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (item.Kind == TargetKind.StoreApp)
                    {
                        uint length = 0; GetApplicationUserModelId(process.Handle, ref length, null);
                        if (length == 0) continue;
                        var buffer = new StringBuilder((int)length);
                        if (GetApplicationUserModelId(process.Handle, ref length, buffer) == 0 && buffer.ToString().Equals(item.Target, StringComparison.OrdinalIgnoreCase)) return RunningState.Running;
                    }
                    else if (item.ProcessName.Length > 0 && item.ProcessPath.Length == 0) return RunningState.Running;
                    else if (process.MainModule?.FileName is { } path && Validation.NormalizePath(path) == expectedPath) return RunningState.Running;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { unknown = true; }
            }
        }
        return unknown ? RunningState.Unknown : RunningState.NotRunning;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint applicationUserModelIdLength, StringBuilder? applicationUserModelId);
}
