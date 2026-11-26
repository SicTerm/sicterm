using Sic.App.Controls;
using Sic.Core.Appearance;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// The zoomed pane (Ctrl+Shift+Z, <see cref="PaneZoom"/>): a tab's focused pane shown as a card over its other panes, which stay
/// tiled under a dim, and put back by the same key. The zoom is the tab's, saved with it, and follows the keyboard: Alt+Arrow, a
/// click on a dimmed pane or a pane chosen in a panel moves it to that pane, so the keys that move between panes, tabs and
/// workspaces work as they do unzoomed. A pane that joins or leaves the tab ends it. The card is a share of the window's body or
/// of the terminal area (Settings → Zoomed pane), and in the window's body it lies over the rail and the panel too.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The z-order of the terminal area while the tab showing has a zoomed pane: above the rail (2), the panel (2) and its edge (3), so the card lies over them.</summary>
    private const int LiftedWork = 4;

    /// <summary>Ctrl+Shift+Z: zooms the selected tab's focused pane, or puts the zoomed pane back.</summary>
    private void ToggleZoom()
    {
        if (SelectedHost is not { Panes.Count: > 0 } host) return;
        SetZoomed(host, host.Content.Zoomed is not null ? null : FocusedPane(host));
    }

    /// <summary>
    /// Zooms <paramref name="pane"/> in its tab, or with null puts the tab's zoomed pane back, gives the pane the keyboard and
    /// saves the tab. Nothing is started or reloaded: the pane is only laid out again, and its terminal resized.
    /// </summary>
    private void SetZoomed(TabHost host, SessionPane? pane)
    {
        host.Saved = host.Saved with { Zoomed = pane?.Descriptor.Id };
        host.Item.Tag = host.Saved;
        ApplyZoom(host);
        var focus = pane ?? FocusedPane(host);
        SetActivePane(focus);
        if (focus.IsRunning && ReferenceEquals(host, SelectedHost)) focus.FocusInput();
        if (_started && !_closing) SaveLayout();
    }

    /// <summary>
    /// Shows the tab as its saved zoom says: the card where <see cref="ZoomCard"/> puts it, the terminal area lifted over the
    /// sidebar while the tab showing has one, and the panes told whether they are zoomed (their menu's item reads so).
    /// </summary>
    private void ApplyZoom(TabHost host)
    {
        var zoomed = host.Saved.Zoomed is { } id && host.Panes.Any(pane => pane.Descriptor.Id == id) ? id : null;
        host.Content.Zoom(zoomed, zoomed is null ? default : ZoomCard(host.Content));
        foreach (var pane in host.Panes) pane.IsZoomed = pane.Descriptor.Id == zoomed;
        if (ReferenceEquals(host, SelectedHost)) LiftWork(zoomed is not null);
        RefreshActiveRing();
    }

    private void LiftWork(bool lifted) => Canvas.SetZIndex(_work, lifted ? LiftedWork : 0);

    /// <summary>
    /// The zoomed pane's card in <paramref name="canvas"/>'s pixels: the chosen share of the terminal area, or of the window's
    /// body (the rail, the panel and the terminal area, not the status bar) centred on it, kept off the tab strip so the tabs
    /// and the title bar's drag area stay uncovered. A canvas not in the window yet takes its own area; the next layout corrects it.
    /// </summary>
    private PaneRect ZoomCard(PaneCanvas canvas)
    {
        var area = new PaneRect(0, 0, canvas.ActualWidth, canvas.ActualHeight);
        var percent = _preferences.PaneZoomPercent;
        if (PaneZoom.Normalize(_preferences.PaneZoom) == PaneZoom.Area || !canvas.IsLoaded) return PaneZoom.Card(area, percent);
        PaneRect frame;
        try
        {
            var origin = _body.TransformToVisual(canvas).TransformPoint(new Windows.Foundation.Point(0, 0));
            frame = new PaneRect(origin.X, origin.Y, _body.ActualWidth, _body.ActualHeight);
        }
        catch (ArgumentException) { return PaneZoom.Card(area, percent); }
        var card = PaneZoom.Card(frame, percent);
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
