using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace StartFlow.Windows;

/// <summary>Reads shell links through IShellLinkW without executing or resolving their targets.</summary>
public static class WindowsShortcut
{
    public static ShortcutDetails Read(string path)
    {
        var instance = new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var workingDirectory = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH; do not resolve or contact a target.
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            return new(target.ToString(), arguments.ToString(), workingDirectory.ToString(), icon.Length == 0 ? "" : $"{icon},{iconIndex}");
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    /// <summary>Creates a Unicode shell link; also used to construct real Windows regression fixtures.</summary>
    public static void Write(string path, string target, string arguments, string workingDirectory)
    {
        var instance = new ShellLink();
        try
        {
            var link = (IShellLinkW)instance;
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDirectory);
            ((IPersistFile)instance).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
