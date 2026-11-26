using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Settings → Window opacity (November 12, 2026; docs/decisions/window-transparency.md): below 100 %, Windows blends
/// the whole window with what is behind it, the terminals and their text included, as a layered window. It is the one way a
/// terminal here can be seen through: WinUI's WebView2 paints a solid color where its page is clear, so a see-through terminal
/// background is not to be had. Unlike the frosted chrome it shows over Remote Desktop too. At 100 % the window is not layered.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The least solid the window goes, in percent; below this the terminals stop being readable over a busy desktop.</summary>
    internal const int WindowOpacityMin = 50;
    private TextBlock? _windowOpacityValue;

    internal static int ClampWindowOpacity(int percent) => Math.Clamp(percent, WindowOpacityMin, 100);

    /// <summary>Saved and applied at once, as the slider moves.</summary>
    internal void SetWindowOpacity(int percent)
    {
        percent = ClampWindowOpacity(percent);
        if (_preferences.WindowOpacity != percent)
        {
            _preferences.WindowOpacity = percent;
            SavePreferences();
        }
        ApplyWindowOpacity();
        if (_windowOpacityValue is not null) _windowOpacityValue.Text = percent + "%";
    }

    /// <summary>
    /// Puts the saved opacity on the window. Also called after the window changes presenter (full screen and back), which may
    /// rewrite the window's styles.
    /// </summary>
    private void ApplyWindowOpacity()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(window, ExtendedStyle);
        var layered = (style & LayeredStyle) != 0;
        if (_preferences.WindowOpacity >= 100)
        {
            if (!layered) return;
            SetWindowLongPtr(window, ExtendedStyle, style & ~LayeredStyle);
            RedrawWindow(window, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0400);
            return;
        }
        if (!layered) SetWindowLongPtr(window, ExtendedStyle, style | LayeredStyle);
        SetLayeredWindowAttributes(window, 0, (byte)Math.Round(255 * _preferences.WindowOpacity / 100.0), 2);
    }

    /// <summary>What Windows has for the window: whether it is layered, and the alpha it is blended at (255 when it is not).</summary>
    internal (bool Layered, byte Alpha) ReadWindowOpacity()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if ((GetWindowLongPtr(window, ExtendedStyle) & LayeredStyle) == 0) return (false, 255);
        return GetLayeredWindowAttributes(window, out _, out var alpha, out var flags) && (flags & 2) != 0 ? (true, alpha) : (true, (byte)255);
    }

    private const int ExtendedStyle = -20;
    private const nint LayeredStyle = 0x80000;
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);
}
