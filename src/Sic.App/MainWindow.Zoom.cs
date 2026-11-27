using Sic.App.Controls;
using Sic.Core.Appearance;
using Sic.Core.State;

namespace Sic.App;

/// <summary>
/// The zoomed pane (Ctrl+Shift+Z, <see cref="PaneZoom"/>): the focused pane shown as a card over its tab's other panes, which
/// stay tiled under a dim, until the same key puts it back. The zoom is the window's, not one tab's: while it is on it stays
/// with the keyboard, so Alt+Arrow, a click on a dimmed pane, a pane chosen in a panel, Ctrl+Tab and a workspace switched to
/// each zoom the pane they bring forward, and a pane that joins or leaves the tab passes it to the one focused. Each tab keeps
/// its card while it is not shown (<see cref="SavedTab.Zoomed"/>), so going back to it resizes nothing; zooming out puts every
/// tab's back. The card is a share of the terminal area (Settings → Zoomed pane size).
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Whether the zoom is on: the tab shown has its focused pane as a card. Restored as on when the layout has a zoomed tab.</summary>
    private bool _zoomOn;

    /// <summary>Ctrl+Shift+Z: zooms the selected tab's focused pane, or puts every zoomed pane back.</summary>
    private void ToggleZoom()
    {
        if (SelectedHost is not { Panes.Count: > 0 } host) return;
        SetZoomOn(!_zoomOn, host, FocusedPane(host));
    }

    /// <summary>Turns the zoom on with <paramref name="pane"/> zoomed in <paramref name="host"/>, or off, every tab's card put back.</summary>
    private void SetZoomOn(bool on, TabHost host, SessionPane pane)
    {
        _zoomOn = on;
        if (on) { SetZoomed(host, pane); return; }
        foreach (var other in _open.Values.Where(item => !ReferenceEquals(item, host) && item.Saved.Zoomed is not null).ToList())
        {
            other.Saved = other.Saved with { Zoomed = null };
            other.Item.Tag = other.Saved;
            ApplyZoom(other);
        }
        SetZoomed(host, null);
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

    /// <summary>Shows the tab as its saved zoom says, the card where <see cref="ZoomCard"/> puts it, and tells the panes whether they are zoomed (their menu's item reads so).</summary>
    private void ApplyZoom(TabHost host, bool animate = false)
    {
        var zoomed = host.Saved.Zoomed is { } id && host.Panes.Any(pane => pane.Descriptor.Id == id) ? id : null;
        foreach (var pane in host.Panes) pane.IsZoomed = pane.Descriptor.Id == zoomed;
        host.Content.Zoom(zoomed, zoomed is null ? default : ZoomCard(host.Content), animate && ReferenceEquals(host, SelectedHost));
        RefreshActiveRing();
    }

    /// <summary>
    /// A tab is shown, from the strip, Ctrl+Tab or a workspace switched to: with the zoom on, its focused pane is zoomed, or the
    /// card it kept is placed again; with it off, the tab is shown tiled.
    /// </summary>
    private void ShowZoom(TabHost host)
    {
        if (_zoomOn && host.Panes.Count > 0 && !host.Panes.Any(pane => pane.Descriptor.Id == host.Saved.Zoomed))
        {
            host.Saved = host.Saved with { Zoomed = FocusedPane(host).Descriptor.Id };
            host.Item.Tag = host.Saved;
        }
        ApplyZoom(host);
    }

    /// <summary>The zoomed pane's card in <paramref name="canvas"/>'s pixels: the chosen share of the terminal area, centred in it.</summary>
    private PaneRect ZoomCard(PaneCanvas canvas) => PaneZoom.Card(new PaneRect(0, 0, canvas.ActualWidth, canvas.ActualHeight), _preferences.PaneZoomPercent);

    /// <summary>After every layout of the window: the showing tab's card follows the terminal area, which resizes with the window, the panel and the sidebar. Laid out again only when the card has changed.</summary>
    private void FollowZoomFrame()
    {
        if (SelectedHost is not { Content: { Zoomed: { } zoomed } canvas }) return;
        var card = ZoomCard(canvas);
        if (card != canvas.ZoomCard) canvas.Zoom(zoomed, card);
    }

    /// <summary>The keyboard moved to <paramref name="pane"/>: with the zoom on, the zoom goes with it.</summary>
    private void FollowZoom(SessionPane pane)
    {
        if (_zoomOn && HostOf(pane) is { } host && host.Content.Zoomed != pane.Descriptor.Id) SetZoomed(host, pane);
    }

    /// <summary>A press on a dimmed pane behind the card: that pane takes the zoom and the keyboard; the press never reaches its terminal.</summary>
    private void ZoomPressed(TabHost host, string id)
    {
        if (host.Panes.FirstOrDefault(pane => pane.Descriptor.Id == id) is not { } pane) return;
        ForgetSelectedFolder();
        SetZoomed(host, pane);
    }

    /// <summary>Settings → Zoomed pane size changed: every zoomed tab's card is made again.</summary>
    private void RefreshZooms()
    {
        foreach (var host in _open.Values.Where(item => item.Saved.Zoomed is not null)) ApplyZoom(host);
    }
}
