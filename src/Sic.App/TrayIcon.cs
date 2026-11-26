using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// The notification-area icon and the window subclass behind it. Besides tray clicks it watches the window's
/// minimize command so the app can hide to the tray instead of the taskbar when Settings → Close and minimize says so.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private const uint SystemCommand = 0x0112;
    private const nuint MinimizeCommand = 0xF020;
    private const nuint SubclassId = 0x43434e54;
    private readonly nint _window;
    private readonly Action _show, _quit, _hide;
    private readonly Action? _endSession, _shellMetricsChanged, _environmentChanged;
    private readonly Action<string>? _open;
    private readonly Func<bool> _minimizeToTray;
    private readonly SubclassProcedure _procedure;
    private readonly uint _taskbarCreated;
    private string? _iconPath;
    private NotifyIconData _data;
    private nint _ownedIcon;
    private bool _disposed;

    /// <param name="shellMetricsChanged">Called when the taskbar may have changed scaling, so the window can reload its own
    /// taskbar icon at the new size.</param>
    /// <param name="open">Called with the text of an Open in SIC request another launch handed this window (<see cref="Sic.Core.Shell.OpenRequest"/>).</param>
    /// <param name="environmentChanged">Called when Windows says the user's or the PC's variables changed, as an installer does
    /// after adding to Path (WM_SETTINGCHANGE with "Environment"), on the UI thread.</param>
    internal TrayIcon(nint window, Action show, Action quit, Func<bool> minimizeToTray, Action hide, string? iconPath = null, Action? endSession = null, Action? shellMetricsChanged = null, Action<string>? open = null, Action? environmentChanged = null)
    {
        _window = window; _show = show; _quit = quit; _minimizeToTray = minimizeToTray; _hide = hide; _endSession = endSession; _shellMetricsChanged = shellMetricsChanged; _open = open; _environmentChanged = environmentChanged; _procedure = WindowProcedure;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _iconPath = iconPath;
        var size = ShellIcons.TraySize();
        _ownedIcon = ShellIcons.Load(_iconPath, size);
        IconSize = _ownedIcon != 0 ? size : 0;
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
            Flags = 0x1 | 0x2 | 0x4 | 0x80, Callback = CallbackMessage,
            Icon = _ownedIcon != 0 ? _ownedIcon : LoadIcon(0, 32512), Tip = "SicTerm · click to reopen", Info = "", InfoTitle = ""
        };
        if (!SetWindowSubclass(window, _procedure, SubclassId, 0))
        {
            var error = Marshal.GetLastWin32Error();
            ShellIcons.Destroy(_ownedIcon);
            throw new Win32Exception(error, "Could not attach the tray message handler.");
        }
        if (!Add()) { RemoveWindowSubclass(window, _procedure, SubclassId); ShellIcons.Destroy(_ownedIcon); throw new Win32Exception("The SicTerm tray icon could not be added."); }
    }

    internal bool IsRegistered
    {
        get
        {
            var identity = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = _window, Id = 1 };
            return ShellNotifyIconGetRect(ref identity, out _) == 0;
        }
    }

    /// <summary>The frame size the tray icon was last loaded at (ShellIcons.TraySize); 0 when it fell back to the system icon.</summary>
    internal int IconSize { get; private set; }

    /// <summary>The .ico the tray icon comes from.</summary>
    internal string? IconPath => _iconPath;

    /// <summary>Shows another .ico in the tray (Settings → App icon), at the size the tray draws now, then frees the old icon.</summary>
    internal void SetIconPath(string path)
    {
        var size = ShellIcons.TraySize();
        var icon = ShellIcons.Load(path, size);
        if (icon == 0) return;
        _iconPath = path;
        var old = _ownedIcon;
        _ownedIcon = icon; _data.Icon = icon; IconSize = size;
        ShellNotifyIcon(1, ref _data);
        ShellIcons.Destroy(old);
    }

    /// <summary>Reloads the icon when the taskbar's scaling has changed since it was loaded, then frees the old one.</summary>
    private void RefreshIconSize()
    {
        var size = ShellIcons.TraySize();
        if (size == IconSize || _ownedIcon == 0) return;
        var icon = ShellIcons.Load(_iconPath, size);
        if (icon == 0) return;
        var old = _ownedIcon;
        _ownedIcon = icon; _data.Icon = icon; IconSize = size;
        ShellNotifyIcon(1, ref _data);
        ShellIcons.Destroy(old);
    }

    /// <summary>
    /// Puts the icon on the taskbar rather than in the hidden icons the first time Windows shows it, and leaves it wherever
    /// the user has put it since (<see cref="TrayPlacement"/>). Windows writes the icon's entry a moment after the icon is
    /// added, so this looks a few times. Returns the entry's <c>IsPromoted</c> afterwards, or null when no entry appeared.
    /// </summary>
    internal static async Task<int?> PlaceOnTaskbarAsync()
    {
        if (Environment.ProcessPath is not { } executable) return null;
        foreach (var wait in (int[])[0, 1000, 3000, 10000])
        {
            if (wait > 0) await Task.Delay(wait);
            try
            {
                using var settings = Registry.CurrentUser.OpenSubKey(TrayPlacement.SettingsKey);
                if (settings is null || TrayPlacement.Find(Entries(settings), executable, 1, KnownFolder) is not { } entry) continue;
                if (!TrayPlacement.ShouldPromote(entry)) return entry.IsPromoted;
                using var key = settings.OpenSubKey(entry.Name, writable: true);
                if (key is null) continue;
                key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                Diagnostics.Log("tray icon put on the taskbar, its first time shown");
                return 1;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Diagnostics.Log("tray icon placement: " + exception.Message);
                return null;
            }
        }
        return null;
    }

    private static List<TrayPlacement.Entry> Entries(RegistryKey settings)
    {
        var entries = new List<TrayPlacement.Entry>();
        foreach (var name in settings.GetSubKeyNames())
        {
            using var key = settings.OpenSubKey(name);
            if (key is null) continue;
            entries.Add(new TrayPlacement.Entry(name, key.GetValue("ExecutablePath") as string, key.GetValue("UID") is int uid ? uid : null,
                key.GetValue("IconGuid") is not null, key.GetValue("IsPromoted") is int promoted ? promoted : null));
        }
        return entries;
    }

    private static string? KnownFolder(Guid id)
    {
        nint path = 0;
        try { return SHGetKnownFolderPath(id, 0, 0, out path) == 0 ? Marshal.PtrToStringUni(path) : null; }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    internal void RequestWindowClose() => PostMessage(_window, 0x0010, 0, 0);

    /// <summary>Posts the same minimize command the caption button and the taskbar send.</summary>
    internal void RequestMinimize() => PostMessage(_window, SystemCommand, MinimizeCommand, 0);

    private bool Add()
    {
        if (!ShellNotifyIcon(0, ref _data)) return false;
        _data.Version = 4;
        ShellNotifyIcon(4, ref _data);
        return true;
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint reference)
    {
        if (!_disposed && message == _taskbarCreated) { RefreshIconSize(); Add(); }
        // A scaling change reaches top-level windows as one of these; the size check makes the rest cheap no-ops.
        if (!_disposed && message is 0x001A or 0x007E or 0x02E0) { RefreshIconSize(); _shellMetricsChanged?.Invoke(); }
        if (!_disposed && message == 0x001A && lParam != 0 && _environmentChanged is not null && Marshal.PtrToStringUni(lParam) == "Environment") _environmentChanged();
        if (!_disposed && message == SingleInstance.ActivateMessage) { Diagnostics.Log("activate message from another launch"); _show(); return 0; }
        if (!_disposed && message == SingleInstance.QuitMessage) { Diagnostics.Log("quit message from another launch"); _quit(); return 0; }
        if (!_disposed && message == SingleInstance.CopyDataMessage && _open is not null && lParam != 0)
        {
            // The sender's buffer lives only for this call, so the text is copied out before returning; 1 tells it the request was taken.
            var data = Marshal.PtrToStructure<SingleInstance.CopyData>(lParam);
            if (data.Id == Sic.Core.Shell.OpenRequest.CopyDataId && data.Data != 0 && data.Size >= 2)
            {
                var text = Marshal.PtrToStringUni(data.Data, (int)(data.Size / 2)).TrimEnd('\0');
                Diagnostics.Log("open request received: " + text.Replace('\n', ' '));
                _open(text);
                return 1;
            }
        }
        if (!_disposed && message == 0x0011 && _endSession is not null) { Diagnostics.Log("end of session (WM_ENDSESSION " + wParam + ")"); try { _endSession(); } catch (Exception exception) when (exception is not OutOfMemoryException) { } }
        if (!_disposed && message == SystemCommand && (wParam & 0xFFF0) == MinimizeCommand && _minimizeToTray())
        {
            try { _hide(); } catch (Exception exception) when (exception is not OutOfMemoryException) { return DefSubclassProc(window, message, wParam, lParam); }
            return 0;
        }
        if (!_disposed && message == CallbackMessage)
        {
            var notification = (uint)((long)lParam & 0xffff);
            if (notification is 0x400 or 0x403 or 0x0203) _show();
            else if (notification is 0x007b or 0x0205) ShowMenu();
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            AppendMenu(menu, 0, 1, "Open SicTerm");
            AppendMenu(menu, 0, 2, "Quit…");
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var selected = TrackPopupMenuEx(menu, 0x0100 | 0x0002, point.X, point.Y, _window, 0);
            PostMessage(_window, 0, 0, 0);
            if (selected == 1) _show();
            else if (selected == 2) _quit();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShellNotifyIcon(2, ref _data);
        ShellIcons.Destroy(_ownedIcon);
        RemoveWindowSubclass(_window, _procedure, SubclassId);
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size; internal nint Window; internal uint Id, Flags, Callback; internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags; internal Guid Guid; internal nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIconIdentifier { internal uint Size; internal nint Window; internal uint Id; internal Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect", ExactSpelling = true)] private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identity, out NativeRect rectangle);
    [DllImport("shell32.dll", ExactSpelling = true)] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint reference);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
