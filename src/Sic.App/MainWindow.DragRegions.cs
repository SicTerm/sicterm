using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sic.Core.State;
using Windows.Graphics;

namespace Sic.App;

/// <summary>
/// Where the window drags from: the rail around its buttons, the panel header beside Hide panel, the head of the tab strip
/// (the inset before the first tab, or the mark with the sidebar on the right), and the empty strip after the tabs. With
/// many tabs open that last one shrinks to a sliver, so the rest of the chrome with nothing to click takes the drag too,
/// with the usual title-bar manners: double-click maximizes, right-click opens the window menu. With the bars at the bottom
/// there is no title bar: the strip drags from down there as it does at the top, and the rail as always. See
/// docs/decisions/window-drag-areas.md for what was measured.
/// </summary>
internal sealed partial class MainWindow
{
    private InputNonClientPointerSource? _nonClient;
    private PixelRect[] _dragRects = [];
    private int _dialogsOpen;
    private bool _dragScaleTracked;
    private bool _dragUpdateQueued;
    // Re-reads the areas while a flyout or menu is open, since closing one need not lay anything out.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _popupWatch;

    /// <summary>
    /// SetTitleBar takes one element and the window needs four areas, so the window owns its whole caption list instead,
    /// through InputNonClientPointerSource, and rewrites it after any layout pass that moved something: the panel sliding or
    /// being resized, a tab added, the window resized or maximized. LayoutUpdated fires for every layout pass anywhere in the
    /// window (a status line's text, a pane header's path), several to a frame while something animates, so the passes of one
    /// dispatcher turn are answered with one recomputation, once the turn's layout is done. A change of display scale moves
    /// pixels without moving DIPs, so it is watched on its own. Until the first write nothing drags: the TabView clears
    /// Windows' default caption band when it loads.
    /// </summary>
    private void TrackDragRegions()
    {
        _root.LayoutUpdated += (_, _) =>
        {
            if (_dragUpdateQueued) return;
            _dragUpdateQueued = true;
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { _dragUpdateQueued = false; UpdateDragRegions(); })) _dragUpdateQueued = false;
        };
        _root.Loaded += (_, _) =>
        {
            if (!_dragScaleTracked) { _dragScaleTracked = true; _root.XamlRoot.Changed += (_, _) => UpdateDragRegions(); }
            UpdateDragRegions();
        };
    }

    /// <summary>Recomputes the drag areas and hands them to Windows when they changed. A failure leaves the last areas in place rather than take the window down.</summary>
    private void UpdateDragRegions()
    {
        if (_closing || _allowClose || _root.XamlRoot is not { } root || !_root.IsLoaded) return;
        // Minimized, the window's origin is parked off screen; the areas set before still fit the window it restores to.
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized }) return;
        try
        {
            var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(popup => popup.Child is FrameworkElement and not ToolTip).Select(popup => (FrameworkElement)popup.Child).ToArray();
            WatchPopups(popups.Length > 0);
            PixelRect[] rects = [.. DragAreas(root.RasterizationScale, IslandOffset(root), popups)];
            // An empty list brings back Windows' default caption band across the top 32 px, tabs included; a zero rectangle
            // means none, the way WinUI's own title bar says it while a dialog is up.
            if (rects.Length == 0) rects = [default];
            if (rects.SequenceEqual(_dragRects)) return;
            _nonClient ??= InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            _nonClient.SetRegionRects(NonClientRegionKind.Caption, [.. rects.Select(rect => new RectInt32(rect.X, rect.Y, rect.Width, rect.Height))]);
            _dragRects = rects;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidOperationException) { _options.Trace("drag-regions " + exception.Message); }
    }

    /// <summary>
    /// Every drag area, in physical client pixels. Holes are cut by geometry rather than with Passthrough rectangles, which
    /// the smoke's hit test cannot see and which WinUI issue #10151 reports going stale after a scale change. A drag area
    /// sits above everything in the window, so it stands down while a dialog is up and leaves out whatever an open flyout,
    /// menu or dropdown covers.
    /// </summary>
    private List<PixelRect> DragAreas(double scale, NativePoint offset, IReadOnlyList<FrameworkElement> popups)
    {
        var areas = new List<PixelRect>();
        // Full screen has nothing to drag: the window has no frame to move, and its chrome lies over the terminals.
        if (_dialogsOpen > 0 || _fullScreen) return areas;
        PixelRect Box(FrameworkElement element) => PixelBounds(element, scale) is var bounds && bounds.Width > 0 ? bounds with { X = bounds.X + offset.X, Y = bounds.Y + offset.Y } : default;
        // The window's own buttons keep their presses too: with the rail on the right they sit over the rail and the panel header.
        List<PixelRect> covered = [.. popups.Select(Box)];
        if (CaptionButtons() is { Width: > 0 } caption) covered.Add(caption);
        // The panel's resize grip straddles its edge from the top of the window down and keeps its own pointer.
        PixelRect[] grip = _panelEdge.Visibility == Visibility.Visible ? [Box(_panelEdge)] : [];
        areas.AddRange(DragRegions.Subtract(Box(_rail), [.. _railItems.Values.Select(item => Box(item.Button)), .. covered]));
        // The body is wider than the slot mid-slide and reaches under the rail; only what the slot shows counts.
        if (_panel.Visibility == Visibility.Visible)
            areas.AddRange(DragRegions.Subtract(DragRegions.Intersect(Box(_panelHeader), Box(_panel)), [Box(_panelHide), .. grip, .. covered]));
        // The strip's head over the strip's real height, which the backdrop follows (FitStripBackdrop), at the top or the bottom.
        var strip = Box(_stripBackdrop);
        var head = Box(_stripHeader);
        PixelRect[] chip = _workspaceChip is { Visibility: Visibility.Visible } ? [Box(_workspaceChip)] : [];
        areas.AddRange(DragRegions.Subtract(new PixelRect(head.X, strip.Y, head.Width, strip.Height), [.. chip, .. grip, .. covered]));
        areas.AddRange(DragRegions.Subtract(Box(_titleBar), covered));
        return areas;
    }

    /// <summary>The caption buttons' corner in physical client pixels (they are laid over the client area, not the island); empty while there are none.</summary>
    private PixelRect CaptionButtons()
    {
        var bar = AppWindow.TitleBar;
        return bar.RightInset > 0 && bar.Height > 0 ? new PixelRect(AppWindow.ClientSize.Width - bar.RightInset, 0, bar.RightInset, bar.Height) : default;
    }

    /// <summary>
    /// Where the XAML island's origin sits in the client area, in physical pixels: one pixel down while the window is
    /// restored, where WinUI keeps the top border visible, and at the origin when maximized. Without it every area and every
    /// hole would sit a pixel above its element, so a press on a button's bottom row would drag the window.
    /// </summary>
    private NativePoint IslandOffset(XamlRoot root)
    {
        var island = root.CoordinateConverter.ConvertLocalToScreen(new Windows.Foundation.Point(0, 0));
        var client = new NativePoint();
        return ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this), ref client) ? new NativePoint { X = island.X - client.X, Y = island.Y - client.Y } : default;
    }

    /// <summary>An element's laid-out bounds in physical pixels from the island's origin; empty while it is not laid out or not shown.</summary>
    private static PixelRect PixelBounds(FrameworkElement element, double scale)
    {
        if (!element.IsLoaded || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return default;
        var bounds = element.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return DragRegions.FromDips(bounds.X, bounds.Y, bounds.Width, bounds.Height, scale);
    }

    private void WatchPopups(bool open)
    {
        if (!open) { _popupWatch?.Stop(); return; }
        if (_popupWatch is null)
        {
            _popupWatch = DispatcherQueue.CreateTimer();
            _popupWatch.Interval = TimeSpan.FromMilliseconds(250);
            _popupWatch.Tick += (_, _) => UpdateDragRegions();
        }
        if (!_popupWatch.IsRunning) _popupWatch.Start();
    }

    /// <summary>
    /// Verification: whether Windows routes a press at a point of <paramref name="element"/>, in DIPs from its top-left, to
    /// the window's caption. The point is mapped to the screen by the island's own converter, independently of how the
    /// areas were computed, and WM_NCHITTEST sent to the window answers from the same list the input sink uses; it answers
    /// while the window is hidden, so the smoke can ask it without taking the mouse. Keep points 8 DIP or more below the
    /// window's top edge: the resize border's top 4 px answer HTTOP while the window is restored.
    /// </summary>
    private bool IsDragPoint(FrameworkElement element, double x, double y) => HitTest(element, x, y) == 2;

    /// <summary>What Windows answers <c>WM_NCHITTEST</c> with at a point of <paramref name="element"/>: 2 for a drag area, 1 for the client area, and so on.</summary>
    private nint HitTest(FrameworkElement element, double x, double y)
    {
        var screen = _root.XamlRoot!.CoordinateConverter.ConvertLocalToScreen(element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(x, y)));
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        const uint NonClientHitTest = 0x0084;
        return SendMessage(window, NonClientHitTest, 0, (nint)(((screen.Y & 0xFFFF) << 16) | (screen.X & 0xFFFF)));
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
