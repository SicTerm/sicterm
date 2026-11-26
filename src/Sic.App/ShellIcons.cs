using System.Runtime.InteropServices;

namespace Sic.App;

/// <summary>
/// The sizes the Windows shell draws SIC's icon at, and loading the matching .ico frame. The notification area uses the
/// small-icon size (16 px at 100% scaling, 20 at 125%, 24 at 150%, 32 at 200%) and the taskbar button 24 px at 100%
/// (30, 36, 42, 48), or 16 px × scaling on a small taskbar. Both sit on the primary taskbar, so that monitor's scaling
/// decides, not the monitor the window is on. The title bar's small icon, which Alt+Tab and the taskbar's thumbnail also
/// show, follows the window's own monitor. A frame loaded at exactly these sizes is drawn 1:1; anything else is resampled,
/// and the taskbar's resampling of the 32 px big-icon frame down to 24 px is what made the button look soft.
/// </summary>
internal static class ShellIcons
{
    internal static int TraySize() { var size = GetSystemMetricsForDpi(49, TaskbarDpi()); return size > 0 ? size : 16; }

    /// <summary>
    /// 24 px × scaling on the standard taskbar (48 px thick at 100%), the primary case. A taskbar under 40 px thick is the
    /// small one (Settings → Personalization → Taskbar → Taskbar behaviors → Taskbar size: Small), which draws button icons
    /// at 16 px × scaling: measured with Icon Branding Runway on September 30, 2026, where a 32 px taskbar drew a 24 px
    /// frame shrunk to 16 and an exact 16 px frame 1:1. A standard taskbar that shrinks its buttons when it fills up
    /// (Show smaller taskbar buttons: When taskbar is full) keeps its height, so it cannot be told apart from here; it
    /// shrinks the 24 px frame itself.
    /// </summary>
    internal static int TaskbarButtonSize()
    {
        var dpi = TaskbarDpi();
        return (int)Math.Round((TaskbarThickness(dpi) < 40 ? 16 : 24) * dpi / 96.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>The window's small icon (SM_CXSMICON) at the window's own DPI: 16 px at 100% scaling.</summary>
    internal static int SmallIconSize(nint window)
    {
        var dpi = GetDpiForWindow(window);
        var size = GetSystemMetricsForDpi(49, dpi > 0 ? dpi : 96u);
        return size > 0 ? size : 16;
    }

    /// <summary>The window's scaling: 1.0 at 100%, 1.5 at 150%.</summary>
    internal static double WindowScale(nint window) { var dpi = GetDpiForWindow(window); return dpi > 0 ? dpi / 96.0 : 1.0; }

    /// <summary>
    /// Whether the taskbar is light enough for the near-black icon: Settings → Personalization → Colors, Windows' own mode
    /// (SystemUsesLightTheme), unless Show accent color on Start and taskbar is on (ColorPrevalence), when the accent's Start
    /// colour (StartColorMenu) decides by its lightness, as <see cref="Sic.Core.Appearance.AppIcon.LightTaskbar"/> explains.
    /// It reads the colour Windows paints with rather than sampling the screen, which reads black while the session is
    /// locked. Windows sends WM_SETTINGCHANGE when any of them changes.
    /// </summary>
    internal static bool LightTaskbar()
    {
        using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var lightMode = personalize?.GetValue("SystemUsesLightTheme") is int mode && mode == 1;
        var accent = personalize?.GetValue("ColorPrevalence") is int prevalence && prevalence == 1;
        using var colours = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        return Sic.Core.Appearance.AppIcon.LightTaskbar(lightMode, accent, colours?.GetValue("StartColorMenu") is int start ? start : null);
    }

    /// <summary>
    /// Whether File Explorer's menus are light: Windows' app mode (AppsUseLightTheme, Choose your default app mode), which the
    /// menus follow, as distinct from the taskbar's mode in <see cref="LightTaskbar"/>. Light when the value is missing, as Windows is.
    /// </summary>
    internal static bool LightMenus()
    {
        using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return personalize?.GetValue("AppsUseLightTheme") is not int mode || mode != 0;
    }

    /// <summary>Loads the frame of an .ico file at the given size as an HICON the caller owns; 0 when the file is missing.</summary>
    internal static nint Load(string? path, int size) => path is not null && File.Exists(path) ? LoadImage(0, path, 1, size, size, 0x10) : 0;

    internal static void Destroy(nint icon) { if (icon != 0) DestroyIcon(icon); }

    private static uint TaskbarDpi()
    {
        var monitor = MonitorFromWindow(FindWindow("Shell_TrayWnd", null), 1);
        return monitor != 0 && GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0 ? x : 96u;
    }

    /// <summary>The primary taskbar's thickness at 100% scaling: 48 px for the standard taskbar, 32 for the small one.</summary>
    private static double TaskbarThickness(uint dpi) =>
        GetWindowRect(FindWindow("Shell_TrayWnd", null), out var rect) ? Math.Min(rect.Right - rect.Left, rect.Bottom - rect.Top) * 96.0 / dpi : 48;

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
}
