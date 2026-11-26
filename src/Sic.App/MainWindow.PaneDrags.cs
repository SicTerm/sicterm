using Sic.App.Controls;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Sic.App;

/// <summary>Where a dragged pane would land: on a pane of <see cref="Host"/> (<see cref="Drop"/>), or in the strip before tab <see cref="StripIndex"/>; <see cref="Refusal"/> says why it may not, "" for a refusal that needs no words.</summary>
internal sealed record PaneDragTarget(TabHost? Host, PaneDrop? Drop, int? StripIndex, string? Refusal)
{
    internal bool Allowed => Refusal is null;
}

/// <summary>
/// A pane dragged by its header: to another side of a pane in its own tab (a split made the wrong way put right), into
/// another tab shown by resting on its header as a tab drag does, or to the strip (up, or down with the tabs at the bottom),
/// where it becomes a tab of its own where an accent bar shows. The drag is the window's own, on a pointer captured by the
/// root, not WinUI's drag and drop: the strip never delivered that drag's events, and a tab shown mid-drag unloads the pane
/// that would be carrying it. The marks are the ones a tab drag shows. Esc, or a lost capture, ends the drag with nothing
/// moved. docs/decisions/tab-drops.md.
/// </summary>
internal sealed partial class MainWindow
{
    private SessionPane? _draggedPane;
    private TabHost? _dragHome;
    private Pointer? _dragPointer;
    // The accent bar in the strip where a dragged pane would become a tab, over the tab view, never hit-tested.
    private readonly Canvas _stripOverlay = new() { IsHitTestVisible = false };
    private readonly Border _stripBar = new() { Width = 3, CornerRadius = new CornerRadius(1.5), Visibility = Visibility.Collapsed };
    private int _stripIndex;

    private FrameworkElement BuildStripOverlay()
    {
        _stripBar.Background = _theme.Accent;
        _stripOverlay.Children.Add(_stripBar);
        return _stripOverlay;
    }

    private void WatchPaneDrags()
    {
        _root.PointerMoved += (_, args) =>
        {
            if (_draggedPane is null || _dragPointer is null || args.Pointer.PointerId != _dragPointer.PointerId) return;
            TrackPaneDrag(args.GetCurrentPoint(_tabs).Position);
            args.Handled = true;
        };
        _root.PointerReleased += (_, args) =>
        {
            if (_draggedPane is null || _dragPointer is null || args.Pointer.PointerId != _dragPointer.PointerId) return;
            args.Handled = true;
            DropPaneDrag(args.GetCurrentPoint(_tabs).Position);
        };
        _root.PointerCaptureLost += (_, args) => { if (_draggedPane is not null && _dragPointer is not null && args.Pointer.PointerId == _dragPointer.PointerId) CancelPaneDrag("the capture was lost"); };
        _root.PointerCanceled += (_, args) => { if (_draggedPane is not null && _dragPointer is not null && args.Pointer.PointerId == _dragPointer.PointerId) CancelPaneDrag("the pointer was cancelled"); };
        _root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (_draggedPane is null || args.Key != Windows.System.VirtualKey.Escape) return;
            args.Handled = true;
            CancelPaneDrag("Esc");
        }), handledEventsToo: true);
    }

    /// <summary>A pane's header has been dragged past the threshold: the root takes the pointer and the drag begins.</summary>
    private void BeginPaneDrag(SessionPane pane, PointerRoutedEventArgs args)
    {
        if (_draggedPane is not null || _draggedTab is not null || HostOf(pane) is not { } home || home.Closing || _closingTabs.Contains(home.Item) || pane.Closing || _closingPanes.Contains(pane)) return;
        if (!_root.CapturePointer(args.Pointer)) return;
        _draggedPane = pane; _dragHome = home; _dragPointer = args.Pointer;
        _sprung = false;
        _seen.Clear();
        Diagnostics.Log("pane drag: begun on “" + pane.DisplayName + "”");
        TrackPaneDrag(args.GetCurrentPoint(_tabs).Position);
    }

    /// <summary>Verification: begins a pane drag as a header's press would, without the pointer.</summary>
    internal void BeginPaneDragForCheck(SessionPane pane) { _draggedPane = pane; _dragHome = HostOf(pane); _dragPointer = null; _sprung = false; _seen.Clear(); }

    /// <summary>Verification: a step of the drag at <paramref name="point"/>, in the tab view's pixels, with the marks it shows.</summary>
    internal void TrackPaneDragForCheck(Point point) => TrackPaneDrag(point);

    /// <summary>Verification: the drag released at <paramref name="point"/>; true when something moved.</summary>
    internal bool EndPaneDragForCheck(Point point) => DropPaneDrag(point);

    /// <summary>The strip bar's place while it shows, as the index of the tab it stands before, for verification; null while hidden.</summary>
    internal int? StripBarIndex => _stripBar.Visibility == Visibility.Visible ? _stripIndex : null;

    /// <summary>
    /// Where a pane dragged to <paramref name="point"/> (in the tab view's pixels) would land. On the strip, at the top or at the
    /// bottom: before the tab whose middle the point is left of, refused without words for a pane that is its tab's only one,
    /// which is a tab already. Off it: the pane of the tab showing under the point and the side of it nearest, refused as a tab's
    /// drop is, and without words on the dragged pane itself.
    /// </summary>
    internal PaneDragTarget PaneDragTargetAt(SessionPane pane, Point point)
    {
        if (OnStrip(point))
        {
            var index = StripIndexAt(point.X);
            var home = HostOf(pane);
            return new(null, null, index, home is null ? "The pane is no longer open" : home.Panes.Count < 2 ? "" : _tabs.TabItems.Count >= Workspaces.MaxTabs ? $"A workspace holds up to {Workspaces.MaxTabs} tabs" : null);
        }
        if (SelectedHost is not { } host) return new(null, null, null, "Not a tab");
        var origin = host.Content.TransformToVisual(_tabs).TransformPoint(new Point(0, 0));
        var drop = host.Content.DropAt(new Point(point.X - origin.X, point.Y - origin.Y));
        return new(host, drop, null, PaneDropRefusal(pane, host, drop));
    }

    /// <summary>Why a pane may not be dropped there, or null; "" on the pane itself, where the mark alone says it.</summary>
    internal string? PaneDropRefusal(SessionPane pane, TabHost? target, PaneDrop? drop)
    {
        if (target is null) return "Not a tab";
        if (HostOf(pane) is not { } home) return "The pane is no longer open";
        if (target.Closing || _closingTabs.Contains(target.Item)) return "This tab is closing";
        if (drop is null) return "No pane here";
        if (ReferenceEquals(home, target) && drop.Pane == pane.Descriptor.Id) return "";
        // A pane moving within its own tab leaves the count as it is.
        return RoomRefusal(target, drop, ReferenceEquals(home, target) ? target.Panes.Count : target.Panes.Count + 1);
    }

    /// <summary>The place in the strip a drop at <paramref name="x"/> would take: before the first tab whose middle lies right of it, else after the last.</summary>
    internal int StripIndexAt(double x)
    {
        var index = 0;
        foreach (var item in _tabs.TabItems.OfType<TabViewItem>())
        {
            if (!item.IsLoaded || item.ActualWidth <= 0) { index++; continue; }
            var origin = item.TransformToVisual(_tabs).TransformPoint(new Point(0, 0));
            if (x < origin.X + item.ActualWidth / 2) break;
            index++;
        }
        return index;
    }

    /// <summary>A step of the drag: the marks for where it would land, and the hover that shows a tab rested on.</summary>
    private void TrackPaneDrag(Point point)
    {
        if (_draggedPane is not { } pane) return;
        var target = PaneDragTargetAt(pane, point);
        if (target.StripIndex is { } index)
        {
            foreach (var host in _open.Values) host.Content.HideDrop();
            if (target.Allowed) ShowStripBar(index); else HideStripBar();
            HoverAt(point, "pane-drag");
            return;
        }
        HideStripBar();
        StopHover(null);
        foreach (var host in _open.Values) if (!ReferenceEquals(host, target.Host)) host.Content.HideDrop();
        if (target.Host is null || target.Drop is null) { target.Host?.Content.HideDrop(); return; }
        target.Host.Content.ShowDrop(target.Drop.Area, target.Refusal ?? pane.DisplayName, target.Allowed);
    }

    private void ShowStripBar(int index)
    {
        var tabs = _tabs.TabItems.OfType<TabViewItem>().ToList();
        double x;
        if (index < tabs.Count) x = tabs[index].TransformToVisual(_tabs).TransformPoint(new Point(0, 0)).X;
        else if (tabs.Count > 0) { var last = tabs[^1]; x = last.TransformToVisual(_tabs).TransformPoint(new Point(0, 0)).X + last.ActualWidth; }
        else x = 0;
        _stripIndex = index;
        var strip = StripHeightNow;
        _stripBar.Height = Math.Max(8, strip - 12);
        Canvas.SetLeft(_stripBar, x - 1.5);
        // 6 px inside the strip wherever it lies: the overlay spans the terminal area, whose bottom the strip may run along.
        Canvas.SetTop(_stripBar, _stripBackdrop.TransformToVisual(_stripOverlay).TransformPoint(default).Y + 6);
        _stripBar.Visibility = Visibility.Visible;
    }

    private void HideStripBar() => _stripBar.Visibility = Visibility.Collapsed;

    /// <summary>The drag released: the move the marks promised, then the drag's end. True when something moved.</summary>
    private bool DropPaneDrag(Point point)
    {
        if (_draggedPane is not { } pane) return false;
        var target = PaneDragTargetAt(pane, point);
        var home = _dragHome;
        EndPaneDrag();
        var moved = false;
        try
        {
            if (!target.Allowed) { }
            else if (target.StripIndex is { } index) moved = MovePaneToNewTab(pane, index) is not null;
            else if (target.Host is { } host && target.Drop is { } drop) { MovePaneInto(pane, host, drop.Pane, drop.Side); moved = true; }
        }
        catch (Exception exception) { ShowError(exception.Message); }
        Diagnostics.Log("pane drag: released " + (moved ? "and moved" : "with nothing moved") + (target.Refusal is { Length: > 0 } why ? " (" + why + ")" : ""));
        if (!moved && _sprung && home is not null && _open.ContainsKey(home.Item)) ShowHost(home);
        _sprung = false;
        return moved;
    }

    private void CancelPaneDrag(string why)
    {
        var home = _dragHome;
        EndPaneDrag();
        Diagnostics.Log("pane drag: ended by " + why);
        if (_sprung && home is not null && _open.ContainsKey(home.Item)) ShowHost(home);
        _sprung = false;
    }

    /// <summary>The drag's state and marks cleared, and the pointer given back.</summary>
    private void EndPaneDrag()
    {
        var pointer = _dragPointer;
        _draggedPane = null; _dragHome = null; _dragPointer = null;
        StopHover(null);
        HideStripBar();
        foreach (var host in _open.Values) host.Content.HideDrop();
        if (pointer is not null) _root.ReleasePointerCapture(pointer);
    }
}
