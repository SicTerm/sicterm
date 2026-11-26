using Sic.Core.State;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Sic.App;

/// <summary>
/// Full screen, on F11 (docs/decisions/full-screen.md): the window fills its display with no frame, through Windows' own
/// full-screen presenter, which draws no title bar and no caption buttons. The tabs, the panes and the status bar stay where
/// they are. The rail and its panel leave their edge, and the terminal area takes the window's width across their columns;
/// they come back over the terminals, which keep their width, while the pointer is at that edge or in them, and while a panel
/// opened from the keyboard has the keyboard. One button, Leave full screen, stands at the strip's end where SIC's window
/// buttons go with the bars at the bottom; minimize and close are not there, for the tabs' sake. Nothing is saved: a launch is
/// windowed, as it was left. Settings → Autohide sidebar puts the rail away the same way in the window while the panel is shut
/// (<see cref="SidebarOverlays"/>).
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>How far in from the window's edge, in DIPs, the pointer brings the sidebar out.</summary>
    private const double SidebarEdge = 10;
    /// <summary>How long the sidebar the pointer brought out stays out with the pointer away from it, before it may go again.</summary>
    private const int SidebarGraceMilliseconds = 600;
    private long _sidebarGraceUntil;
    /// <summary>How far past the sidebar, in DIPs, the pointer still counts as on it: a drift just past the icons sends nothing away.</summary>
    private const double SidebarKeep = 12;
    /// <summary>How long the pointer has to be away from the sidebar it visited before the sidebar goes: an overshoot comes back within it.</summary>
    private const int SidebarLingerMilliseconds = 200;
    private long _sidebarLeftAt;
    private bool _fullScreen;
    /// <summary>Whether the window was maximized before full screen, to be maximized again after it.</summary>
    private bool _fullScreenWasMaximized;
    // In full screen: whether the rail (and the panel, when open) are over the terminals now, and whether they were brought out
    // from the keyboard, which keeps them out while the keyboard is in them. Out of full screen the sidebar is always out.
    private bool _sidebarOut = true, _sidebarHeld;
    /// <summary>Counts the sidebar's comings and goings, so a slide away that was overtaken by a return leaves nothing collapsed.</summary>
    private int _sidebarMoves;
    // The window's base under the rail and the panel while they lie over the terminals: the rail is translucent, laid over Mica
    // or the base in a window, and over a terminal its text would show through.
    private readonly Border _sidebarBacking = new() { Visibility = Visibility.Collapsed };
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sidebarWatch;
    /// <summary>How often the pointer is looked at in full screen: the rail's slide starts within this of the pointer reaching the edge.</summary>
    private static readonly TimeSpan SidebarWatchInterval = TimeSpan.FromMilliseconds(50);
    /// <summary>The rail alone sliding in from its edge and away, shorter than the panel's slides: it is 48 DIPs and should feel immediate.</summary>
    private static readonly TimeSpan RailOutDuration = TimeSpan.FromMilliseconds(100), RailAwayDuration = TimeSpan.FromMilliseconds(80);
    // The rail's and the backing's shift from their place while the rail alone slides in full screen; at rest, 0.
    private readonly TranslateTransform _railShift = new(), _backingShift = new();
    private Storyboard? _railMotion;

    /// <summary>F11, the terminal page's F11, and Leave full screen at the strip's end with the bars at the bottom.</summary>
    internal void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        if (on)
        {
            if (AppWindow.Presenter is not OverlappedPresenter windowed) return;
            _fullScreenWasMaximized = windowed.State == OverlappedPresenterState.Maximized;
            // The bounds to come back to, kept here: the presenter change arrives as a size change at the display's bounds while
            // the presenter still reads as the windowed one, which placement tracking would otherwise record as the window's
            // bounds (seen on October 18, 2026: F11 twice left the window at the display's size). Tracking rests while _fullScreen.
            if (!_fullScreenWasMaximized) _restoredBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            _fullScreen = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            ApplyWindowOpacity();
            PlaceWork();
            HideSidebar(animate: false);
            WatchSidebar(true);
        }
        else
        {
            var bounds = _restoredBounds;
            WatchSidebar(false);
            // Still _fullScreen through the presenter change and the move back, so neither is recorded as a placement.
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            ApplyWindowOpacity();
            if (_fullScreenWasMaximized) (AppWindow.Presenter as OverlappedPresenter)?.Maximize();
            else if (bounds.Width > 0 && (AppWindow.Size.Width != bounds.Width || AppWindow.Size.Height != bounds.Height || AppWindow.Position.X != bounds.X || AppWindow.Position.Y != bounds.Y))
                AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            _fullScreen = false;
            // The placement the window has now, in case its changes came while tracking rested.
            RecordPlacement();
            // With Autohide sidebar on and the panel shut, the rail stays away in the window too.
            if (SidebarOverlays) AutoHideSidebar(animate: false);
            else
            {
                PlaceWork();
                RevealSidebar();
                _sidebarLeftAt = 0;
                // Out already (the pointer in it, or a panel held out), the sidebar keeps its column: the backing and any slide go.
                _sidebarHeld = false;
                RestRail();
                _sidebarBacking.Visibility = Visibility.Collapsed;
            }
        }
        Diagnostics.Log("full screen: " + (on ? "on" : "off"));
        UpdateCaptionInset();
        PlaceWindowButtons();
        UpdateDragRegions();
    }

    /// <summary>
    /// Whether the rail lies over the terminals and comes out only while the pointer is at its edge: in full screen, and in the
    /// window with Settings → Autohide sidebar on while the panel is shut (November 11, 2026). An open panel docks the sidebar
    /// in its column in the window, as with the setting off.
    /// </summary>
    private bool SidebarOverlays => _fullScreen || _preferences.SidebarAutoHide && !_panelOpen;

    /// <summary>
    /// The rail away from its column in the window, as full screen puts it: the terminals across its column, and the pointer at
    /// its edge bringing it out over them. Only while <see cref="SidebarOverlays"/>; full screen does this itself.
    /// </summary>
    private void AutoHideSidebar(bool animate)
    {
        if (_fullScreen || !SidebarOverlays) return;
        PlaceWork();
        HideSidebar(animate);
        WatchSidebar(true);
        UpdateCaptionInset();
        UpdateDragRegions();
    }

    /// <summary>The rail back in its column in the window, out of the way of nothing: a panel opening, or Autohide sidebar turned off.</summary>
    private void DockSidebar()
    {
        if (_fullScreen) return;
        WatchSidebar(false);
        _sidebarHeld = false;
        // A slide away under way ends here, not with the rail collapsed after it.
        _sidebarMoves++;
        RestRail();
        _sidebarOut = true;
        _rail.Visibility = Visibility.Visible;
        _sidebarBacking.Visibility = Visibility.Collapsed;
        PlaceWork();
        UpdateCaptionInset();
        UpdateDragRegions();
    }

    /// <summary>
    /// The terminal area in its column beside the rail and the panel, or, while the rail overlays (<see cref="SidebarOverlays"/>), across all three, where the rail and
    /// the panel lie over its edge (their z-order is set in <see cref="BuildShell"/>) and take nothing from its width.
    /// </summary>
    private void PlaceWork()
    {
        // Lying over the terminals, the rail, its backing and the panel start below the tab strip (above it with the tabs at
        // the bottom), so they never cover the workspace indicator or the tabs, which stay at the terminals' edge (November 11,
        // 2026, at Chris's request); in their column they span the window's height.
        var clear = SidebarOverlays ? (TabsBottom ? new Thickness(0, 0, 0, BottomStripHeight) : new Thickness(0, StripHeight, 0, 0)) : new Thickness(0);
        _rail.Margin = _sidebarBacking.Margin = _panel.Margin = clear;
        _panelEdge.Margin = new Thickness(_panelEdge.Margin.Left, clear.Top, _panelEdge.Margin.Right, clear.Bottom);
        Grid.SetColumn(_work, SidebarOverlays || SidebarRight ? 0 : 2);
        Grid.SetColumnSpan(_work, SidebarOverlays ? 3 : 1);
    }

    /// <summary>
    /// The rail, and the panel when it is open, shown: in their column out of full screen, over the terminals in it, where the
    /// open panel slides out from the rail as Menu Animation says (<see cref="PanelSlides"/>) unless <paramref name="slide"/>
    /// is off, for a caller that slides it itself. <paramref name="held"/> for a panel opened from the keyboard, which keeps them
    /// out while the keyboard is in them.
    /// </summary>
    private void RevealSidebar(bool held = false, bool slide = true)
    {
        if (held) _sidebarHeld = true;
        if (_sidebarOut) return;
        _sidebarOut = true;
        _sidebarMoves++;
        var slides = slide && SidebarOverlays && PanelSlides;
        // The rail alone slides in from its edge; with the panel open it is in place at once and the panel slides out from it.
        if (slides && !_panelOpen) _ = SlideRailAsync(outward: true);
        else
        {
            RestRail();
            _rail.Visibility = Visibility.Visible;
            _sidebarBacking.Visibility = SidebarOverlays ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_panelOpen)
        {
            // The slide starts from where the panel is: at nothing when it was away, or part-way when it was on its way out.
            if (slides) _ = SlidePanelAsync(true, animate: true);
            else
            {
                if (_panelMotion is null) _panel.Width = _panelWidth;
                _panel.Visibility = _panelEdge.Visibility = Visibility.Visible;
            }
        }
        UpdateCaptionInset();
    }

    /// <summary>
    /// The rail and the panel away, in full screen, with the open panel's width kept for its return: the open panel slides shut
    /// first as Menu Animation says, and the rail goes with it once it has, unless <paramref name="animate"/> is off (entering
    /// full screen). The pointer back at the edge meanwhile brings them out again from where the panel is.
    /// </summary>
    private void HideSidebar(bool animate = true)
    {
        _sidebarHeld = false;
        if (!_sidebarOut) return;
        _sidebarOut = false;
        var move = ++_sidebarMoves;
        if (animate && SidebarOverlays && PanelSlides && _rail.Visibility == Visibility.Visible)
        {
            if (_panelOpen && _panel.Visibility == Visibility.Visible) { _ = SlideSidebarAwayAsync(move); return; }
            if (!_panelOpen) { _ = SlideRailAwayAsync(move); return; }
        }
        StopPanelMotion();
        RestRail();
        _rail.Visibility = _panel.Visibility = _panelEdge.Visibility = _sidebarBacking.Visibility = Visibility.Collapsed;
        if (_panelOpen) _panel.Width = _panelWidth;
        UpdateCaptionInset();
    }

    /// <summary>The panel slid shut, then the rail and the backing away, unless the sidebar moved again meanwhile.</summary>
    private async Task SlideSidebarAwayAsync(int move)
    {
        await SlidePanelAsync(false, animate: true);
        if (_sidebarMoves != move || _closing) return;
        _rail.Visibility = _panel.Visibility = _panelEdge.Visibility = _sidebarBacking.Visibility = Visibility.Collapsed;
        if (_panelOpen) _panel.Width = _panelWidth;
        UpdateCaptionInset();
    }

    /// <summary>The rail alone slid to its edge, then collapsed with the backing, unless the sidebar moved again meanwhile.</summary>
    private async Task SlideRailAwayAsync(int move)
    {
        await SlideRailAsync(outward: false);
        if (_sidebarMoves != move || _closing) return;
        RestRail();
        _rail.Visibility = _sidebarBacking.Visibility = Visibility.Collapsed;
        UpdateCaptionInset();
    }

    /// <summary>Where the rail and the backing are while away: a rail's width beyond their edge.</summary>
    private double RailAway => SidebarRight ? RailWidth : -RailWidth;

    /// <summary>
    /// Slides the rail and its backing in from their edge, shown, or to it, where they stay shown until the caller collapses
    /// them. A slide interrupted part-way carries on from where it is, in the time the remaining distance would have taken; the
    /// task ends when the rail is at rest.
    /// </summary>
    private async Task SlideRailAsync(bool outward)
    {
        if (!ReferenceEquals(_rail.RenderTransform, _railShift)) { _rail.RenderTransform = _railShift; _sidebarBacking.RenderTransform = _backingShift; }
        var target = outward ? 0 : RailAway;
        var from = _rail.Visibility == Visibility.Visible ? _railShift.X : RailAway;
        StopRailMotion();
        _railShift.X = _backingShift.X = from;
        _rail.Visibility = _sidebarBacking.Visibility = Visibility.Visible;
        if (Math.Abs(target - from) < 0.5) { _railShift.X = _backingShift.X = target; return; }
        var whole = outward ? RailOutDuration : RailAwayDuration;
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, whole.TotalMilliseconds * Math.Abs(target - from) / RailWidth));
        var motion = new Storyboard();
        foreach (var shift in new[] { _railShift, _backingShift })
        {
            var animation = new DoubleAnimation
            {
                From = from, To = target, Duration = new Duration(duration), EnableDependentAnimation = true,
                EasingFunction = new ExponentialEase { EasingMode = outward ? EasingMode.EaseOut : EasingMode.EaseIn, Exponent = outward ? 5 : 4 }
            };
            Storyboard.SetTarget(animation, shift);
            Storyboard.SetTargetProperty(animation, "X");
            motion.Children.Add(animation);
        }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        motion.Completed += (_, _) => completed.TrySetResult();
        _railMotion = motion;
        motion.Begin();
        // The compositor does not tick while the window is hidden; a slide that never completes must not leave the rail half-way.
        await Task.WhenAny(completed.Task, Task.Delay(duration + TimeSpan.FromSeconds(1)));
        if (!ReferenceEquals(_railMotion, motion)) return;
        _railMotion = null;
        motion.Stop();
        _railShift.X = _backingShift.X = target;
    }

    /// <summary>Stops a rail slide where it is: the shift under way is kept, since stopping the storyboard would drop it.</summary>
    private void StopRailMotion()
    {
        if (_railMotion is not { } motion) return;
        _railMotion = null;
        var x = _railShift.X;
        motion.Stop();
        _railShift.X = _backingShift.X = x;
    }

    /// <summary>The rail and the backing in their place, any slide stopped: for a show or a hide without one.</summary>
    private void RestRail()
    {
        StopRailMotion();
        _railShift.X = _backingShift.X = 0;
    }

    /// <summary>
    /// Looks at the pointer twenty times a second while the window is full screen. The smoke drives <see cref="TendSidebar"/>
    /// with points of its own instead, so the real pointer, wherever it rests, decides nothing there.
    /// </summary>
    private void WatchSidebar(bool on)
    {
        if (!on) { _sidebarWatch?.Stop(); return; }
        if (_options.SmokeTest) return;
        if (_sidebarWatch is null)
        {
            _sidebarWatch = DispatcherQueue.CreateTimer();
            _sidebarWatch.Interval = SidebarWatchInterval;
            _sidebarWatch.Tick += (_, _) => { if (GetCursorPos(out var cursor)) TendSidebar(cursor); };
        }
        _sidebarWatch.Start();
    }

    /// <summary>
    /// One look at the pointer, in screen pixels. With the sidebar away, the pointer within <see cref="SidebarEdge"/> of the
    /// window's edge on the sidebar's side, beside the terminal area, brings it out. Out, the pointer leaving it sends it
    /// away, except while a dialog, a menu or a drop list is open, while its edge is being dragged, or while a panel opened
    /// from the keyboard still has the keyboard; the pointer visiting the sidebar ends that hold.
    /// </summary>
    private void TendSidebar(NativePoint cursor)
    {
        if (!SidebarOverlays || _closing || _root.XamlRoot is not { } root || !_root.IsLoaded) return;
        if (!_sidebarOut)
        {
            var body = ScreenBounds(_body);
            var edge = Math.Max(1, (int)Math.Round(SidebarEdge * root.RasterizationScale));
            var beside = cursor.Y >= body.Y && cursor.Y < body.Y + body.Height;
            var atEdge = (SidebarRight ? cursor.X >= body.X + body.Width - edge && cursor.X < body.X + body.Width : cursor.X >= body.X && cursor.X < body.X + edge) || OnResizeBorder(cursor, body);
            // Not over Leave full screen at the strip's end, which the sidebar on the right would cover as it came out; and, in
            // the window, only where it is SicTerm under the pointer, not another window over its edge.
            if (beside && atEdge && !Contains(ScreenBounds(_windowButtons), cursor) && (_fullScreen || UnderPointer(cursor)))
            {
                RevealSidebar();
                _sidebarGraceUntil = _options.SmokeTest ? 0 : Environment.TickCount64 + SidebarGraceMilliseconds;
            }
            return;
        }
        if (Contains(ScreenBounds(_rail), cursor) || Contains(ScreenBounds(_panel), cursor) || Contains(ScreenBounds(_panelEdge), cursor)) { _sidebarHeld = false; _sidebarGraceUntil = 0; _sidebarLeftAt = 0; return; }
        // A little past it is still on it (Chris, November 12, 2026: hard to keep open; the rail is 48 DIPs and a pixel past it sent it away).
        var keep = (int)Math.Round(SidebarKeep * root.RasterizationScale);
        if (Near(ScreenBounds(_rail), cursor, keep) || Near(ScreenBounds(_panel), cursor, keep)) { _sidebarLeftAt = 0; return; }
        // On the resize border beside the sidebar the pointer has not left it: the border is what brought it out.
        if (OnResizeBorder(cursor, ScreenBounds(_body))) { _sidebarHeld = false; return; }
        if (_dialogsOpen > 0 || _panelEdge.IsDragging || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is not ToolTip)) return;
        if (_sidebarHeld && FocusInSidebar(root)) return;
        // Just out, it waits a moment for the pointer to come back: a pointer that crossed the edge onto the next display, or
        // past the window, had it open and shut in one movement (Chris, November 12, 2026). A visit to the sidebar ends the wait.
        if (Environment.TickCount64 < _sidebarGraceUntil) return;
        // Away from the sidebar it visited, the pointer has a moment to come back before the sidebar goes. The smoke has no wait.
        if (!_options.SmokeTest)
        {
            var now = Environment.TickCount64;
            if (_sidebarLeftAt == 0) { _sidebarLeftAt = now; return; }
            if (now - _sidebarLeftAt < SidebarLingerMilliseconds) return;
        }
        _sidebarLeftAt = 0;
        HideSidebar();
    }

    /// <summary>
    /// In the window, whether the pointer is on the window's resize border on the sidebar's side, beside the body: the few pixels
    /// outside the body where Windows shows the resize arrow. A pointer pushed to the window's edge lands there as often as on
    /// the body's own edge (Chris, November 12, 2026: the sidebar came out only from a spot a pixel or two inside), so it counts
    /// as the edge, and as the sidebar once that is out. Full screen has no border.
    /// </summary>
    private bool OnResizeBorder(NativePoint cursor, PixelRect body)
    {
        if (_fullScreen || body.Width <= 0 || cursor.Y < body.Y || cursor.Y >= body.Y + body.Height) return false;
        if (!GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var window)) return false;
        return SidebarRight ? cursor.X >= body.X + body.Width && cursor.X < window.Right : cursor.X < body.X && cursor.X >= window.Left;
    }

    /// <summary>Whether the element with the keyboard is in the rail or the panel (a flyout's is not: its popup has no such ancestor).</summary>
    private bool FocusInSidebar(XamlRoot root)
    {
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, _rail) || ReferenceEquals(element, _panel)) return true;
        return false;
    }

    /// <summary>An element's laid-out bounds in screen pixels; empty while it is not shown.</summary>
    private PixelRect ScreenBounds(FrameworkElement element)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0 || _root.XamlRoot is not { } root) return default;
        var transform = element.TransformToVisual(null);
        var topLeft = root.CoordinateConverter.ConvertLocalToScreen(transform.TransformPoint(new Windows.Foundation.Point(0, 0)));
        var bottomRight = root.CoordinateConverter.ConvertLocalToScreen(transform.TransformPoint(new Windows.Foundation.Point(element.ActualWidth, element.ActualHeight)));
        return new PixelRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    /// <summary>
    /// Whether the window at a screen point is this one, not another lying over it. The smoke's points are its own, wherever its
    /// window lies among others, so there it always is.
    /// </summary>
    private bool UnderPointer(NativePoint point) =>
        _options.SmokeTest || GetAncestor(WindowFromPoint(point), RootAncestor) == WinRT.Interop.WindowNative.GetWindowHandle(this);

    private const uint RootAncestor = 2;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);

    private static bool Near(PixelRect rect, NativePoint point, int margin) =>
        rect.Width > 0 && rect.Height > 0 && point.X >= rect.X - margin && point.X < rect.X + rect.Width + margin && point.Y >= rect.Y - margin && point.Y < rect.Y + rect.Height + margin;

    private static bool Contains(PixelRect rect, NativePoint point) => rect.Width > 0 && point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
}
