using Sic.App.Controls;
using Sic.Core.Appearance;
using Sic.Core.State;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Sic.App;

/// <summary>
/// The zoomed pane (Ctrl+Shift+Z, <see cref="PaneZoom"/>): a tab's focused pane shown as a card over its other panes, which stay
/// tiled under a dim, and put back by the same key. The zoom is the tab's, saved with it, and follows the keyboard: Alt+Arrow, a
/// click on a dimmed pane or a pane chosen in a panel moves it to that pane, so the keys that move between panes, tabs and
/// workspaces work as they do unzoomed. A pane that joins or leaves the tab ends it. The card is a share of the display's work
/// area, the window growing to it while needed; of the window's body; or of the terminal area (Settings → Zoomed pane). Outside
/// the terminal area it lies over the rail and the panel too.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The z-order of the terminal area while the tab showing has a zoomed pane: above the rail (2), the panel (2) and its edge (3), so the card lies over them.</summary>
    private const int LiftedWork = 4;

    /// <summary>Ctrl+Shift+Z: zooms the selected tab's focused pane, or puts the zoomed pane back.</summary>
    private void ToggleZoom()
    {
        if (SelectedHost is not { Panes.Count: > 0 } host) return;
        SetZoomed(host, host.Saved.Zoomed is not null ? null : FocusedPane(host));
    }

    /// <summary>
    /// Zooms <paramref name="pane"/> in its tab, or with null puts the tab's zoomed pane back, gives the pane the keyboard and
    /// saves the tab. Nothing is started or reloaded: the pane is only laid out again, and its terminal resized. The card grows
    /// out of the pane's place and shrinks back into it as Menu Animation says (<see cref="PanelSlides"/>); everything else that
    /// shows a zoom (a tab switched to, a restore, a resize, a setting) places it at once.
    /// </summary>
    private void SetZoomed(TabHost host, SessionPane? pane)
    {
        host.Saved = host.Saved with { Zoomed = pane?.Descriptor.Id };
        host.Item.Tag = host.Saved;
        ApplyZoom(host, animate: PanelSlides);
        var focus = pane ?? FocusedPane(host);
        SetActivePane(focus);
        if (focus.IsRunning && ReferenceEquals(host, SelectedHost)) focus.FocusInput();
        if (_started && !_closing) SaveLayout();
    }

    /// <summary>
    /// Shows the tab as its saved zoom says: the card where <see cref="ZoomCard"/> puts it, the terminal area lifted over the
    /// sidebar while the tab showing has one, and the panes told whether they are zoomed (their menu's item reads so). For the
    /// Screen frame a window too small for the card is grown first and the card placed once the body has its new size; zooming
    /// out, the terminal area comes down, and a grown window goes back, once the card has shrunk into its place.
    /// </summary>
    private void ApplyZoom(TabHost host, bool animate = false)
    {
        var zoomed = host.Saved.Zoomed is { } id && host.Panes.Any(pane => pane.Descriptor.Id == id) ? id : null;
        foreach (var pane in host.Panes) pane.IsZoomed = pane.Descriptor.Id == zoomed;
        var showing = ReferenceEquals(host, SelectedHost);
        if (showing && zoomed is not null && _started && !_closing && PaneZoom.Normalize(_preferences.PaneZoom) == PaneZoom.Screen && GrowForZoom())
        {
            Canvas.SetZIndex(_work, LiftedWork);
            _ = PlaceZoomAfterGrowthAsync(host, animate);
            RefreshActiveRing();
            return;
        }
        var moved = host.Content.Zoom(zoomed, zoomed is null ? default : ZoomCard(host.Content), animate && showing);
        if (showing)
        {
            if (zoomed is null && !moved.IsCompleted) _ = LowerWorkAfterAsync(host, moved);
            else LiftWork(zoomed is not null);
        }
        RefreshActiveRing();
    }

    /// <summary>The window has just grown for the card: the card is placed once the body has been laid out at its new size, or after half a second at most.</summary>
    private async Task PlaceZoomAfterGrowthAsync(TabHost host, bool animate)
    {
        var resized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Resized(object sender, SizeChangedEventArgs args) => resized.TrySetResult();
        _body.SizeChanged += Resized;
        try { await Task.WhenAny(resized.Task, Task.Delay(500)); }
        finally { _body.SizeChanged -= Resized; }
        if (ReferenceEquals(host, SelectedHost) && host.Saved.Zoomed is not null) ApplyZoom(host, animate);
    }

    /// <summary>Zoomed out with motion: the card stays over the sidebar until it is back in its place, and only then does the terminal area come down and a grown window go back.</summary>
    private async Task LowerWorkAfterAsync(TabHost host, Task moved)
    {
        await moved;
        if (ReferenceEquals(host, SelectedHost) && host.Content.Zoomed is null) LiftWork(false);
    }

    /// <summary>
    /// The showing tab has a zoomed pane, or has none: the terminal area lifted over the sidebar or not, and for the Screen frame
    /// the window grown to the work area while it needs the room, or put back (<see cref="FitWindowToZoom"/>).
    /// </summary>
    private void LiftWork(bool lifted)
    {
        Canvas.SetZIndex(_work, lifted ? LiftedWork : 0);
        FitWindowToZoom(lifted);
    }

    /// <summary>
    /// The window's bounds before the Screen frame grew it and after (physical pixels, as <c>AppWindow</c> reports them), while it
    /// is grown; null otherwise. What is saved as the window's placement meanwhile is the bounds before (<see cref="RecordPlacement"/>).
    /// </summary>
    private (PixelRect Before, PixelRect After)? _zoomGrowth;

    private PixelRect AppWindowBounds => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

    /// <summary>The display's work area in DIPs, for the Screen frame's card; forgotten when the window moves or resizes, which may change display or scaling.</summary>
    private (double Width, double Height)? _zoomWorkArea;

    private (double Width, double Height) ZoomWorkArea()
    {
        if (_zoomWorkArea is { } known) return known;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var scale = _root.XamlRoot?.RasterizationScale ?? 1;
        return (_zoomWorkArea = (area.Width / scale, area.Height / scale)).Value;
    }

    /// <summary>The tab strip's height, which the card keeps clear of.</summary>
    private double ZoomStrip => TabsBottom ? BottomStripHeight : StripHeight;

    /// <summary>
    /// The Screen frame: a window whose body cannot hold the card (<see cref="PaneZoom.ScreenCardFits"/>) is grown to its display's
    /// work area while a pane is zoomed, its visible edges at the work area's, and put back where it was once none is, unless it
    /// was moved, resized, maximized or put in full screen meanwhile, which keeps the window as the user left it. A maximized or
    /// full-screen window is never grown: it has the room already. Any other frame puts a grown window back.
    /// </summary>
    private void FitWindowToZoom(bool zoomed)
    {
        if (!_started || _closing) return;
        if (zoomed && PaneZoom.Normalize(_preferences.PaneZoom) == PaneZoom.Screen) GrowForZoom();
        else ShrinkAfterZoom();
    }

    /// <summary>Grows the window for the Screen frame's card when it needs the room; true when it did now.</summary>
    private bool GrowForZoom()
    {
        if (_zoomGrowth is not null || _fullScreen || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return false;
        var (workWidth, workHeight) = ZoomWorkArea();
        if (_body.ActualWidth <= 0 || PaneZoom.ScreenCardFits(_body.ActualWidth, _body.ActualHeight, workWidth, workHeight, _preferences.PaneZoomPercent, ZoomStrip)) return false;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        // The window's rectangle takes in its invisible resize borders; the visible frame is put at the work area's edges.
        var (left, top, right, bottom) = InvisibleBorders();
        var before = AppWindowBounds;
        AppWindow.MoveAndResize(new RectInt32(area.X - left, area.Y - top, area.Width + left + right, area.Height + top + bottom));
        _zoomGrowth = (before, AppWindowBounds);
        Diagnostics.Log($"zoom: window grown to the work area for the Screen frame, from {before.X},{before.Y} {before.Width}x{before.Height}");
        return true;
    }

    private void ShrinkAfterZoom()
    {
        if (_zoomGrowth is not { } growth) return;
        _zoomGrowth = null;
        if (_fullScreen || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored } || AppWindowBounds != growth.After)
        {
            Diagnostics.Log("zoom: the grown window was moved, resized, maximized or made full screen, so it stays as it is");
            return;
        }
        AppWindow.MoveAndResize(new RectInt32(growth.Before.X, growth.Before.Y, growth.Before.Width, growth.Before.Height));
    }

    /// <summary>The widths of the window's invisible resize borders, left, top, right and bottom, in physical pixels: its rectangle less DWM's visible frame.</summary>
    private (int Left, int Top, int Right, int Bottom) InvisibleBorders()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!GetWindowRect(window, out var outer) || DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var frame, System.Runtime.InteropServices.Marshal.SizeOf<WindowBounds>()) != 0) return (0, 0, 0, 0);
        return (Math.Max(0, frame.Left - outer.Left), Math.Max(0, frame.Top - outer.Top), Math.Max(0, outer.Right - frame.Right), Math.Max(0, outer.Bottom - frame.Bottom));
    }

    private const int DwmwaExtendedFrameBounds = 9;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out WindowBounds value, int size);

    /// <summary>
    /// The zoomed pane's card in <paramref name="canvas"/>'s pixels: the chosen share of the terminal area; of the window's body
    /// (the rail, the panel and the terminal area, not the status bar), centred on it; or the share of the display's work area,
    /// centred on the body and no bigger than it. Kept off the tab strip so the tabs and the title bar's drag area stay
    /// uncovered. A canvas not in the window yet takes its own area; the next layout corrects it.
    /// </summary>
    private PaneRect ZoomCard(PaneCanvas canvas)
    {
        var area = new PaneRect(0, 0, canvas.ActualWidth, canvas.ActualHeight);
        var percent = _preferences.PaneZoomPercent;
        var choice = PaneZoom.Normalize(_preferences.PaneZoom);
        if (choice == PaneZoom.Area || !canvas.IsLoaded) return PaneZoom.Card(area, percent);
        PaneRect frame;
        try
        {
            var origin = _body.TransformToVisual(canvas).TransformPoint(new Windows.Foundation.Point(0, 0));
            frame = new PaneRect(origin.X, origin.Y, _body.ActualWidth, _body.ActualHeight);
        }
        catch (ArgumentException) { return PaneZoom.Card(area, percent); }
        PaneRect card;
        if (choice == PaneZoom.Screen)
        {
            var (workWidth, workHeight) = ZoomWorkArea();
            card = PaneZoom.ScreenCard(frame, workWidth, workHeight, percent);
        }
        else card = PaneZoom.Card(frame, percent);
        // The strip lies beyond the canvas's top edge (its bottom edge with the tabs at the bottom).
        if (!TabsBottom && card.Y < 0) card = card with { Height = Math.Max(0, card.Bottom), Y = 0 };
        if (TabsBottom && card.Bottom > area.Height) card = card with { Height = Math.Max(0, area.Height - card.Y) };
        return card;
    }

    /// <summary>
    /// After every layout of the window: the showing tab's card follows the window's body and the terminal area, which move and
    /// resize with the window, the panel and the sidebar's side. Laid out again only when the card has moved.
    /// </summary>
    private void FollowZoomFrame()
    {
        if (SelectedHost is not { Content: { Zoomed: { } zoomed } canvas }) return;
        var card = ZoomCard(canvas);
        if (card != canvas.ZoomCard) canvas.Zoom(zoomed, card);
    }

    /// <summary>The keyboard moved to <paramref name="pane"/>: in a tab with a zoomed pane, the zoom goes with it.</summary>
    private void FollowZoom(SessionPane pane)
    {
        if (HostOf(pane) is { Content.Zoomed: { } zoomed } host && zoomed != pane.Descriptor.Id) SetZoomed(host, pane);
    }

    /// <summary>A press on a dimmed pane behind the card: that pane takes the zoom and the keyboard; the press never reaches its terminal.</summary>
    private void ZoomPressed(TabHost host, string id)
    {
        if (host.Panes.FirstOrDefault(pane => pane.Descriptor.Id == id) is not { } pane) return;
        ForgetSelectedFolder();
        SetZoomed(host, pane);
    }

    /// <summary>Settings → Zoomed pane or its size changed: every zoomed tab's card is made again.</summary>
    private void RefreshZooms()
    {
        foreach (var host in _open.Values.Where(item => item.Saved.Zoomed is not null)) ApplyZoom(host);
    }
}
