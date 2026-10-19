using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StartFlow.Windows;

public sealed record StartupState(bool Exists, bool Matches, string Command, bool DisabledByWindows);
public sealed class StartupIntegration(string executable, string valueName = "StartFlow")
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string Expected => TargetLauncher.QuoteArgument(executable) + " --login";
    public StartupState Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var command = key?.GetValue(valueName) as string ?? "";
        using var approved = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var bytes = approved?.GetValue(valueName) as byte[];
        return new(command.Length > 0, command.Equals(Expected, StringComparison.OrdinalIgnoreCase), command, bytes is { Length: > 0 } && bytes[0] is 3 or 7);
    }
    public StartupState Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(valueName, Expected, RegistryValueKind.String); else key.DeleteValue(valueName, false);
        var state = Read();
        if (enabled ? !state.Matches : state.Exists) throw new IOException("Startup registry verification failed.");
        return state;
    }
}
public static class LogonIdentity
{
    public static string Current()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 8, out var token)) throw new Win32Exception();
        try
        {
            var size = Marshal.SizeOf<TokenStatistics>();
            var memory = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, 10, memory, size, out _)) throw new Win32Exception();
                var data = Marshal.PtrToStructure<TokenStatistics>(memory);
                return $"{data.AuthenticationId.HighPart:x8}{data.AuthenticationId.LowPart:x8}";
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally { CloseHandle(token); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenStatistics
    { public Luid TokenId; public Luid AuthenticationId; public long ExpirationTime; public int TokenType; public int ImpersonationLevel; public uint DynamicCharged; public uint DynamicAvailable; public uint GroupCount; public uint PrivilegeCount; public Luid ModifiedId; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr data, int length, out int required);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
