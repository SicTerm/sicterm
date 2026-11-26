using Sic.App.Controls;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Sic.App;

/// <summary>
/// A tab dragged from the strip into the tab that is showing: over a pane, a mark shows the half of it the dragged tab would
/// take, the side of the pane nearest the pointer, and releasing moves the tab's panes there, tiled as they were, and closes
/// the emptied tab (MoveTabInto). Dropping back in the strip still only reorders. A tab whose own content is showing has
/// nothing to be dropped into, so resting the pointer on another tab's header for a moment shows that tab, as a bookmark
/// folder opens under a drag; a drag that then ends in the strip shows the dragged tab again. The reasoning is in
/// docs/decisions/tab-drops.md.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The data format a dragged tab carries, so a drag of files or text from another app is never taken for one.</summary>
    private const string TabDropFormat = "application/x-sic-tab";
    /// <summary>How long a drag rests on another tab's header before that tab is shown.</summary>
    internal static TimeSpan DragHoverToShow { get; set; } = TimeSpan.FromMilliseconds(450);
    private TabHost? _draggedTab;
    // Set once a hover has shown another tab during the drag, so a drag ending in the strip can show the dragged tab again.
    private bool _sprung;
    private (TabHost Source, TabHost Target, string Pane, PaneDirection Side)? _pendingDrop;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hoverTimer;
    private TabViewItem? _hovered;

    private void WatchTabDrops()
    {
        _tabs.CanDragTabs = true;
        _tabs.TabDragStarting += OnTabDragStarting;
        _tabs.TabDragCompleted += (_, _) => DispatcherQueue.TryEnqueue(FinishTabDrag);
        // Over a tab's header, with the handled reorder drag included; over anything else of the strip or the content the hover is dropped.
        _tabs.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnStripDragOver), handledEventsToo: true);
        _tabs.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler((_, _) => StopHover("a drag-leave of the strip")), handledEventsToo: true);
    }

    /// <summary>The drag begins: the tab is marked as the one dragged. A tab on its way out, or with its rename open, is not dragged.</summary>
    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (args.Tab is not { } tab || !_open.TryGetValue(tab, out var host) || host.Closing || host.Editor is not null || _closingTabs.Contains(tab)
            || host.Panes.Any(pane => pane.Closing || _closingPanes.Contains(pane)))
        {
            args.Cancel = true;
            return;
        }
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.SetData(TabDropFormat, host.Saved.Id);
        _draggedTab = host;
        _sprung = false;
        _pendingDrop = null;
        _seen.Clear();
        Diagnostics.Log("tab drag: begun on “" + DisplayTitle(host.Saved) + "”");
        StartDragPoll();
    }

    /// <summary>Whether the drag under way is a tab of this window's.</summary>
    private bool IsTabDrag(DataPackageView data) => _draggedTab is not null && data.Contains(TabDropFormat);

    /// <summary>The canvas takes a dragged tab: the mark follows the pointer, and a drop records where it landed.</summary>
    private void WatchPaneDrops(PaneCanvas canvas)
    {
        canvas.AllowDrop = true;
        canvas.DragEnter += OnPaneDragOver;
        canvas.DragOver += OnPaneDragOver;
        canvas.DragLeave += (_, _) => canvas.HideDrop();
        canvas.Drop += OnPaneDrop;
    }

    private void OnPaneDragOver(object sender, DragEventArgs args)
    {
        if (sender is not PaneCanvas canvas || !IsTabDrag(args.DataView)) return;
        var source = _draggedTab!;
        var target = _open.Values.FirstOrDefault(host => ReferenceEquals(host.Content, canvas));
        var drop = canvas.DropAt(args.GetPosition(canvas));
        var refusal = DropRefusal(source, target, drop);
        // Over its own panes the refused mark says enough by itself; the words were taken off on October 10, 2026.
        var wordless = refusal is not null && ReferenceEquals(source, target);
        args.AcceptedOperation = refusal is null ? DataPackageOperation.Move : DataPackageOperation.None;
        args.DragUIOverride.Caption = wordless ? "" : refusal ?? "Move here";
        args.DragUIOverride.IsCaptionVisible = !wordless;
        args.DragUIOverride.IsGlyphVisible = false;
        if (drop is null) canvas.HideDrop();
        else canvas.ShowDrop(drop.Area, wordless ? "" : refusal ?? DragLabel(source), allowed: refusal is null);
        args.Handled = true;
    }

    private void OnPaneDrop(object sender, DragEventArgs args)
    {
        if (sender is not PaneCanvas canvas) return;
        canvas.HideDrop();
        if (!IsTabDrag(args.DataView)) return;
        var source = _draggedTab!;
        var target = _open.Values.FirstOrDefault(host => ReferenceEquals(host.Content, canvas));
        var drop = canvas.DropAt(args.GetPosition(canvas));
        args.Handled = true;
        if (target is null || drop is null || DropRefusal(source, target, drop) is not null) { args.AcceptedOperation = DataPackageOperation.None; return; }
        args.AcceptedOperation = DataPackageOperation.Move;
        // The move is made once the drag is over, from the queue, not from inside the drop: the strip's reorder is still winding
        // up, and the dragged tab leaves the strip as part of the move.
        _pendingDrop = (source, target, drop.Pane, drop.Side);
        DispatcherQueue.TryEnqueue(FinishTabDrag);
    }

    /// <summary>The name the mark shows for the tab being dragged: the tab's, with its pane count when it holds more than one.</summary>
    private string DragLabel(TabHost source) => DisplayTitle(source.Saved) + (source.Panes.Count > 1 ? $" · {source.Panes.Count} panes" : "");

    /// <summary>
    /// Why the dragged tab may not be dropped where the pointer is, in the words the mark shows, or null when it may: into
    /// itself, into a closing tab, past sixteen panes, or into a pane without the room to split that way.
    /// </summary>
    internal string? DropRefusal(TabHost source, TabHost? target, PaneDrop? drop)
    {
        if (target is null) return "Not a tab";
        if (ReferenceEquals(source, target)) return "This is the tab being moved";
        if (target.Closing || _closingTabs.Contains(target.Item)) return "This tab is closing";
        if (drop is null) return "No pane here";
        return RoomRefusal(target, drop, source.Panes.Count + target.Panes.Count);
    }

    /// <summary>
    /// Why a tab or a pane may not be dropped on a pane of <paramref name="target"/>, for want of room, or null when it may: the
    /// tab would hold more than sixteen panes with <paramref name="panesAfter"/>, or the pane has no room to split that way.
    /// </summary>
    private static string? RoomRefusal(TabHost target, PaneDrop drop, int panesAfter)
    {
        if (panesAfter > PaneLayout.MaxPanes) return $"A tab holds up to {PaneLayout.MaxPanes} panes";
        var placed = target.Content.Arrangement?.Panes.FirstOrDefault(placement => placement.Pane == drop.Pane);
        if (placed is null) return "No pane here";
        var direction = PaneLayout.SplitOf(drop.Side);
        if (!PaneLayout.CanSplit(direction, placed.Rect.Width, placed.Rect.Height, PaneSplitter.GrabSize))
            return direction == PaneSplit.Columns ? "Too narrow to split this way" : "Too short to split this way";
        return null;
    }

    /// <summary>
    /// The drag rests on a tab's header: after <see cref="DragHoverToShow"/> that tab is shown, so the dragged tab can be dropped
    /// into it. The tab is found by where the pointer is, not by the event's source: during a reorder the strip's own list is
    /// the drop target, so the event comes from the list, not from the tab under the pointer (seen October 10, 2026: the hover
    /// never showed a tab). The wait is counted in these events as well as by a timer, since the drag runs its own message
    /// loop and the events keep coming while the pointer rests. The strip reorders as the drag goes: once the pointer crosses
    /// the middle of a tab, that tab slides aside and the dragged tab's own slot comes under the pointer, so the tab hovered is
    /// out from under the pointer within a moment (tried by hand on October 10, 2026: the wait was ended every time). The wait
    /// therefore goes on while the pointer is over the dragged tab's slot or over no tab at all; it ends when the pointer
    /// reaches the header of the tab showing, leaves the strip for the content below, or leaves the window. The third try by
    /// hand, the same day, logged no drag event over the strip at all, so the pointer is also read ten times a second during
    /// the drag (<see cref="PollDragCursor"/>), from the cursor itself, which no drag event has to deliver.
    /// </summary>
    private void OnStripDragOver(object sender, DragEventArgs args) => HoverAt(args.GetPosition(_tabs), "drag-over");

    /// <summary>
    /// A reading of where the pointer is during a tab drag, in the strip's own pixels, from <paramref name="source"/>: the drag
    /// events or the cursor poll. Begins, keeps, ends or completes the hover as the reading says.
    /// </summary>
    internal void HoverAt(Windows.Foundation.Point point, string source)
    {
        // A tab's drag or a pane's (MainWindow.PaneDrags.cs); a dragged tab's own slot keeps a hover going, a dragged pane has none.
        if (_draggedTab is null && _draggedPane is null) return;
        var excluded = _draggedTab?.Item;
        var band = StripBand;
        var onStrip = OnStrip(point);
        var item = onStrip ? TabAt(point) : null;
        NoteSeen(source, onStrip, item, excluded);
        // The content lies below the strip, or above it with the tabs at the bottom.
        if (!onStrip) { StopHover((TabsBottom ? point.Y < band.Top : point.Y >= band.Bottom) ? "the content" : "outside the strip"); return; }
        if (item is not null && (ReferenceEquals(item, _tabs.SelectedItem) || !_open.ContainsKey(item))) { StopHover("the tab showing"); return; }
        if (item is null || ReferenceEquals(item, excluded))
        {
            // The dragged tab's slot, or the strip between tabs: the hover begun on a tab that slid aside goes on.
            if (_hovered is not null && DateTimeOffset.UtcNow - _hoveredSince >= DragHoverToShow) ShowHovered();
            return;
        }
        if (ReferenceEquals(_hovered, item))
        {
            if (DateTimeOffset.UtcNow - _hoveredSince >= DragHoverToShow) ShowHovered();
            return;
        }
        StopHover(null);
        _hovered = item;
        _hoveredSince = DateTimeOffset.UtcNow;
        Diagnostics.Log("tab drag: hover begun on “" + (_open.TryGetValue(item, out var over) ? DisplayTitle(over.Saved) : "?") + "” (" + source + ")");
        var timer = _hoverTimer = DispatcherQueue.CreateTimer();
        timer.Interval = DragHoverToShow > TimeSpan.Zero ? DragHoverToShow : TimeSpan.FromMilliseconds(1);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { if (ReferenceEquals(_hoverTimer, timer)) ShowHovered(); };
        timer.Start();
    }

    private DateTimeOffset _hoveredSince;
    // What each source last reported, so the log has one line per change of what is under the pointer, not one per reading;
    // the words are made only for a change, since the readings come with every pointer move and every poll.
    private readonly Dictionary<string, (bool OnStrip, TabViewItem? Item)> _seen = new(StringComparer.Ordinal);

    /// <summary>The strip's height as laid out, for telling the strip from the content under a point in the tab view's pixels.</summary>
    private double StripHeightNow => _stripBackdrop.ActualHeight > 0 ? _stripBackdrop.ActualHeight : StripHeight;

    /// <summary>The strip's band in the tab view's pixels, top edge to bottom edge: along the top, or along the bottom with the tabs at the bottom (Settings → Tab bar).</summary>
    private (double Top, double Bottom) StripBand
    {
        get
        {
            var height = StripHeightNow;
            var top = TabsBottom ? Math.Max(0, _tabs.ActualHeight - height) : 0;
            return (top, top + height);
        }
    }

    /// <summary>Whether a point in the tab view's pixels lies on the strip.</summary>
    private bool OnStrip(Windows.Foundation.Point point) => StripBand is var band && point.Y >= band.Top && point.Y < band.Bottom && point.X >= 0 && point.X < _tabs.ActualWidth;

    private void NoteSeen(string source, bool onStrip, TabViewItem? item, TabViewItem? excluded)
    {
        var seen = (onStrip, onStrip ? item : null);
        if (_seen.TryGetValue(source, out var last) && last == seen) return;
        _seen[source] = seen;
        var what = !onStrip ? "off the strip" : item is null ? "no tab" : ReferenceEquals(item, excluded) ? "the dragged tab's slot"
            : ReferenceEquals(item, _tabs.SelectedItem) ? "the tab showing" : _open.TryGetValue(item, out var host) ? "“" + DisplayTitle(host.Saved) + "”" : "a tab not open";
        Diagnostics.Log("tab drag: " + source + " sees " + what);
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dragPoll;

    /// <summary>Reads the cursor ten times a second for as long as a tab drag lasts; the drag's own message loop runs the timer.</summary>
    private void StartDragPoll()
    {
        StopDragPoll();
        var timer = _dragPoll = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.IsRepeating = true;
        timer.Tick += (_, _) => { if (ReferenceEquals(_dragPoll, timer)) PollDragCursor(); };
        timer.Start();
    }

    private void StopDragPoll()
    {
        if (_dragPoll is not { } timer) return;
        _dragPoll = null;
        timer.Stop();
    }

    /// <summary>The cursor's place in the strip's pixels, from the screen through the island's converter, handed to the hover.</summary>
    private void PollDragCursor()
    {
        if (_draggedTab is null || _tabs.XamlRoot is not { } root || !GetCursorPos(out var screen)) return;
        try
        {
            var local = root.CoordinateConverter.ConvertScreenToLocal(new Windows.Graphics.PointInt32(screen.X, screen.Y));
            var origin = _tabs.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            HoverAt(new Windows.Foundation.Point(local.X - origin.X, local.Y - origin.Y), "poll");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("tab drag: the cursor poll failed: " + exception.Message); StopDragPoll(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    /// <summary>The hover has lasted: the hovered tab is shown, once, and the wait ends.</summary>
    private void ShowHovered()
    {
        var shown = _hovered;
        StopHover(null);
        if (_draggedTab is null || shown is null || !_open.TryGetValue(shown, out var host) || ReferenceEquals(shown, _tabs.SelectedItem)) return;
        _sprung = true;
        _tabs.SelectedItem = shown;
        Diagnostics.Log("tab drag: showed “" + DisplayTitle(host.Saved) + "” after a hover; now " + (ReferenceEquals(_tabs.SelectedItem, shown) ? "showing" : "not showing"));
    }

    /// <summary>The open tab whose header lies under <paramref name="point"/>, in the strip's own pixels, as the tabs are laid out now; null off every header.</summary>
    internal TabViewItem? TabAt(Windows.Foundation.Point point)
    {
        foreach (var item in _open.Keys)
        {
            if (!item.IsLoaded || item.ActualWidth <= 0) continue;
            var origin = item.TransformToVisual(_tabs).TransformPoint(new Windows.Foundation.Point(0, 0));
            if (point.X >= origin.X && point.X < origin.X + item.ActualWidth && point.Y >= origin.Y && point.Y < origin.Y + item.ActualHeight) return item;
        }
        return null;
    }

    /// <summary>Ends a hover under way, logging what ended it when <paramref name="why"/> is given (a hover begun and ended is rare enough to keep).</summary>
    private void StopHover(string? why = null)
    {
        if (_hovered is not null && why is not null) Diagnostics.Log("tab drag: hover ended at " + why);
        _hovered = null;
        if (_hoverTimer is not { } timer) return;
        _hoverTimer = null;
        timer.Stop();
    }

    /// <summary>
    /// The drag is over, by a drop or otherwise: the marks go, a drop recorded is made, and a drag that showed another tab by
    /// hovering and did not end in a move shows the dragged tab again. Run from the queue after the drop and after the strip's
    /// own completion, whichever comes first; the second run finds nothing to do.
    /// </summary>
    private void FinishTabDrag()
    {
        var dragged = _draggedTab;
        _draggedTab = null;
        StopDragPoll();
        StopHover();
        if (dragged is not null) Diagnostics.Log("tab drag: over" + (_pendingDrop is null ? "" : ", with a drop to make"));
        foreach (var host in _open.Values) host.Content.HideDrop();
        EndTabDrag();
        var moved = false;
        if (_pendingDrop is { } drop)
        {
            _pendingDrop = null;
            try { MoveTabInto(drop.Source, drop.Target, drop.Pane, drop.Side); moved = true; }
            catch (Exception exception) { ShowError(exception.Message); }
        }
        // Back to the tab that was dragged.
        if (!moved && _sprung && dragged is not null && _open.ContainsKey(dragged.Item)) ShowHost(dragged);
        _sprung = false;
    }

    /// <summary>Verification: begins a tab drag as the strip would, without the pointer, so the hover and the drop can be driven.</summary>
    internal void BeginTabDragForCheck(TabHost host) { _draggedTab = host; _sprung = false; _pendingDrop = null; _seen.Clear(); }

    /// <summary>Verification: ends the drag begun by <see cref="BeginTabDragForCheck"/>, with a drop recorded at <paramref name="drop"/> when given.</summary>
    internal void EndTabDragForCheck(TabHost? target = null, PaneDrop? drop = null)
    {
        if (_draggedTab is { } source && target is not null && drop is not null) _pendingDrop = (source, target, drop.Pane, drop.Side);
        FinishTabDrag();
    }
}
