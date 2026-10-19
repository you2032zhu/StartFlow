using Microsoft.Win32;
using System.Runtime.InteropServices;
using StartFlow.Core;

namespace StartFlow.Windows;

public sealed record InstalledApp(string Name, string Target, TargetKind Kind, string Detail, bool CanLaunch);
public sealed record ShortcutDetails(string Target, string Arguments, string WorkingDirectory, string Icon);
public static class InstalledApps
{
    public static Task<List<InstalledApp>> ReadAsync() => StaTask(() =>
    {
        var entries = new List<InstalledApp>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                entries.Add(new(Path.GetFileNameWithoutExtension(path), path, TargetKind.Shortcut, path, true));
        }
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            if (folder != null)
            {
                try
                {
                    dynamic items = folder.Items();
                    try
                    {
                        foreach (dynamic item in items)
                        {
                            try
                            {
                                string id = item.ExtendedProperty("System.AppUserModel.ID") as string ?? "";
                                if (id.Contains('!')) entries.Add(new((string)item.Name, id, TargetKind.StoreApp, id, true));
                            }
                            finally { Marshal.FinalReleaseComObject(item); }
                        }
                    }
                    finally { Marshal.FinalReleaseComObject(items); }
                }
                finally { Marshal.FinalReleaseComObject(folder); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        // Uninstall entries are discovery hints, never execute UninstallString or guess a launch executable.
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) continue;
            foreach (var keyName in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(keyName);
                if (key?.GetValue("DisplayName") is not string name || entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                entries.Add(new(name, "", TargetKind.Application, key.GetValue("InstallLocation") as string ?? "", false));
            }
        }
        return entries.DistinctBy(e => (e.Kind, e.Target, e.Name)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });
    public static ShortcutDetails ReadShortcut(string path) => WindowsShortcut.Read(path);
    public static StartupItem FromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var item = new StartupItem { Name = Path.GetFileNameWithoutExtension(path), Target = path, Kind = Directory.Exists(path) ? TargetKind.Folder : ext switch { ".exe" => TargetKind.Application, ".lnk" => TargetKind.Shortcut, ".ps1" or ".bat" or ".cmd" => TargetKind.Script, _ => TargetKind.File } };
        if (item.Kind == TargetKind.Shortcut)
        {
            var link = ReadShortcut(path);
            // Run the original .lnk so shell metadata, arguments and working directory stay intact.
            item.IconPath = path; item.ProcessPath = link.Target;
            item.Notes = $"Target: {link.Target}\nArguments: {link.Arguments}\nWorking directory: {link.WorkingDirectory}\nIcon: {link.Icon}";
        }
        return item;
    }
    private static Task<T> StaTask<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); return completion.Task;
    }
}
