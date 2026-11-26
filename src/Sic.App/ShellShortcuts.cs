using System.Runtime.InteropServices;

namespace Sic.App;

/// <summary>
/// The icon on SIC's shortcuts, kept in the set for the taskbar's tone. A shortcut's icon is a file, so it cannot follow the
/// taskbar by itself, and the taskbar shows the shortcut's icon, not the window's, for a SIC started from a pin or a shortcut
/// (and Start's list shows it always): with the exe's white set over a light taskbar the mark was unreadable. So SIC points its
/// own shortcuts, those whose target is this exe, in the user's Start menu, on the desktop and among the taskbar's pins, at the
/// .ico for the taskbar as it is now, at launch and when the mode changes, and tells the shell the shortcut changed. Nothing
/// else about a shortcut is touched; the taskbar may show the change only after it is next drawn.
/// </summary>
internal static class ShellShortcuts
{
    /// <summary>The shortcuts that now point at <paramref name="iconPath"/>, having pointed elsewhere; nothing when none needed changing.</summary>
    internal static IReadOnlyList<string> Retone(string executablePath, string iconPath)
    {
        var changed = new List<string>();
        foreach (var folder in Folders())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var shortcut in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                try
                {
                    if (RetoneOne(shortcut, executablePath, iconPath)) changed.Add(shortcut);
                }
                catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException) { /* Someone else's shortcut, or one in use; the next launch tries again. */ }
            }
        }
        return changed;
    }

    private static IEnumerable<string> Folders()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        yield return Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        yield return Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "StartMenu");
    }

    private static bool RetoneOne(string shortcutPath, string executablePath, string iconPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);
            var target = new System.Text.StringBuilder(1024);
            link.GetPath(target, target.Capacity, nint.Zero, 0);
            if (!string.Equals(target.ToString(), executablePath, StringComparison.OrdinalIgnoreCase)) return false;
            var current = new System.Text.StringBuilder(1024);
            link.GetIconLocation(current, current.Capacity, out var index);
            if (index == 0 && string.Equals(current.ToString(), iconPath, StringComparison.OrdinalIgnoreCase)) return false;
            link.SetIconLocation(iconPath, 0);
            ((IPersistFile)link).Save(shortcutPath, true);
            // SHCNE_UPDATEITEM with a path (SHCNF_PATHW), so Explorer, Start and the taskbar read the shortcut's icon again.
            var path = Marshal.StringToHGlobalUni(shortcutPath);
            try { SHChangeNotify(0x00002000, 0x0005, path, nint.Zero); }
            finally { Marshal.FreeHGlobal(path); }
            return true;
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int capacity, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
