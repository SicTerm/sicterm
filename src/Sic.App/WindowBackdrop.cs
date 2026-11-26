using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Sic.Core.Appearance;
using Windows.UI;

namespace Sic.App;

/// <summary>
/// The window's backdrop, by Settings → Chrome (November 12, 2026; docs/decisions/window-transparency.md):
/// solid, Mica as WinUI's MicaBackdrop draws it; frosted, Windows' acrylic, the desktop and the windows behind SicTerm blurred
/// and tinted with the theme's base; clear, nothing at all, so whatever the window does not paint solid shows what is behind
/// it, sharp. The acrylic stays frosted while SicTerm is not the window in use, where WinUI's own DesktopAcrylicBackdrop goes
/// solid; Windows draws the plain tint in its place over Remote Desktop, on battery saver, and with its Transparency effects
/// setting off. The clear backdrop shows over Remote Desktop too.
/// One backdrop serves the window for its whole life and changes what it draws: replacing a window's backdrop with another
/// object and then changing the theme ended the app with XAML's "The parameter is incorrect: target" (seen November 12, 2026).
/// </summary>
internal sealed class WindowBackdrop : SystemBackdrop
{
    /// <summary>How much of the tint lies over the blur. Lower shows more of what is behind the window.</summary>
    internal const float TintOpacity = 0.35f, LuminosityOpacity = 0.6f;
    private readonly nint _window;
    private ICompositionSupportsSystemBackdrop? _target;
    private XamlRoot? _xamlRoot;
    private MicaController? _mica;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _frostConfiguration;
    private Windows.UI.Composition.Compositor? _clearCompositor;
    private SubclassProcedure? _procedure;
    private bool _clearDrawn;
    private string _style;
    private Color _tint;

    /// <param name="window">The window this backdrop serves, whose client area Windows composes with alpha while the backdrop is clear.</param>
    internal WindowBackdrop(nint window, string style, Color tint)
    {
        _window = window;
        _style = ChromeStyle.Normalize(style);
        _tint = Color.FromArgb(255, tint.R, tint.G, tint.B);
    }

    /// <summary>A <see cref="ChromeStyle"/> word. Changed in place on the window it serves.</summary>
    internal string Style
    {
        get => _style;
        set
        {
            value = ChromeStyle.Normalize(value);
            if (_style == value) return;
            _style = value;
            if (_target is null || _xamlRoot is null) return;
            Release(_target);
            Attach(_target, _xamlRoot);
        }
    }

    /// <summary>Whether Windows is composing the window's client area with its alpha, as the clear backdrop has it.</summary>
    internal bool ClearDrawn => _clearDrawn;

    /// <summary>The theme's base, which tints the blur and stands in for it where Windows draws no blur.</summary>
    internal Color Tint
    {
        get => _tint;
        set
        {
            _tint = Color.FromArgb(255, value.R, value.G, value.B);
            if (_acrylic is { } acrylic) { acrylic.TintColor = _tint; acrylic.FallbackColor = _tint; }
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (_target is not null) throw new InvalidOperationException("The window backdrop serves one window.");
        _target = connectedTarget; _xamlRoot = xamlRoot;
        Attach(connectedTarget, xamlRoot);
    }

    private void Attach(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // WinUI keeps this configuration current for the target: the theme, high contrast, and whether the window is in use.
        var defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        if (_style == ChromeStyle.Clear)
        {
            // The target takes a brush of the system's compositor, not WinUI's; one with no color leaves the window's own
            // surface, which Windows then composes with per-pixel alpha (DrawClear).
            EnsureDispatcherQueue();
            _clearCompositor ??= new Windows.UI.Composition.Compositor();
            target.SystemBackdrop = _clearCompositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
            DrawClear(true);
        }
        else if (_style == ChromeStyle.Frosted)
        {
            _frostConfiguration = new SystemBackdropConfiguration { IsInputActive = true, IsHighContrast = defaults.IsHighContrast, Theme = defaults.Theme };
            _acrylic = new DesktopAcrylicController { TintColor = _tint, FallbackColor = _tint, TintOpacity = TintOpacity, LuminosityOpacity = LuminosityOpacity };
            _acrylic.AddSystemBackdropTarget(target);
            _acrylic.SetSystemBackdropConfiguration(_frostConfiguration);
        }
        else
        {
            _mica = new MicaController();
            _mica.AddSystemBackdropTarget(target);
            _mica.SetSystemBackdropConfiguration(defaults);
        }
    }

    private void Release(ICompositionSupportsSystemBackdrop target)
    {
        _frostConfiguration = null;
        if (_mica is { } mica) { mica.RemoveSystemBackdropTarget(target); mica.Dispose(); _mica = null; }
        if (_acrylic is { } acrylic) { acrylic.RemoveSystemBackdropTarget(target); acrylic.Dispose(); _acrylic = null; }
        if (_clearDrawn) { target.SystemBackdrop = null; DrawClear(false); }
    }

    /// <summary>
    /// Has Windows compose the window's client area with the alpha it is drawn with, or stop: "blur behind" over a region that
    /// holds nothing blurs nothing and leaves the alpha, which is how a window is see-through on Windows 8 and later. While it
    /// is on, the window's background is erased to nothing instead of to its class brush, which would show at a resize.
    /// </summary>
    private void DrawClear(bool on)
    {
        _clearDrawn = on;
        if (_window == 0) return;
        if (_procedure is null)
        {
            _procedure = EraseToNothing;
            SetWindowSubclass(_window, _procedure, SubclassId, 0);
        }
        var region = on ? CreateRectRgn(-2, -2, -1, -1) : 0;
        var blur = new BlurBehind { Flags = on ? 3u : 1u, Enable = on ? 1 : 0, Region = region };
        DwmEnableBlurBehindWindow(_window, ref blur);
        if (region != 0) DeleteObject(region);
    }

    private nint EraseToNothing(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        const uint EraseBackground = 0x0014;
        if (message == EraseBackground && _clearDrawn && GetClientRect(window, out var area))
        {
            FillRect((nint)wParam, ref area, GetStockObject(4));
            return 1;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>The system's compositor needs a dispatcher queue of the system's kind on its thread; WinUI's own queue is another kind.</summary>
    private static void EnsureDispatcherQueue()
    {
        if (_queueController != 0 || Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
    }

    /// <summary>Frosted, follows Windows' theme and high contrast, and not whether the window is the one in use.</summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        if (_frostConfiguration is null) return;
        var defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        _frostConfiguration.IsHighContrast = defaults.IsHighContrast;
        _frostConfiguration.Theme = defaults.Theme;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        Release(disconnectedTarget);
        _target = null; _xamlRoot = null;
    }

    private const nuint SubclassId = 0x51C7;
    private static nint _queueController;
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind { internal uint Flags; internal int Enable; internal nint Region; internal int TransitionOnMaximized; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct DispatcherQueueOptions { internal int Size; internal int ThreadType; internal int ApartmentType; }
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint window, ref BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int item);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out NativeRect area);
    [DllImport("user32.dll")] private static extern int FillRect(nint context, ref NativeRect area, nint brush);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint reference);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
