using Sic.Core.State;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Sic.App;

/// <summary>
/// A listed folder's row dragged to another place in the list, in the Folders panel or the Agent Workspaces panel, which share
/// the one order (asked for by Chris on November 9, 2026, who had not found Move up and down on the rows' menus). The drag is the
/// row's own, on a pointer the row captures as it is pressed, beginning the drag once it has moved a few pixels, not WinUI's drag and drop:
/// the trees and session lists are put away meanwhile, an accent line between the rows shows where it would land, the panel scrolls near its edges, and the release
/// moves the folder there (<see cref="MoveFolderTo"/>). A press that does not move stays a click; Esc or a lost capture moves nothing.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>A row being dragged: its folder, the panel's list and scroller, the panel's drop line and rows, and the place it would land.</summary>
    private sealed class FolderDrag
    {
        internal required string Folder { get; init; }
        internal required UIElement Row { get; init; }
        internal required StackPanel List { get; init; }
        internal required ScrollViewer Scroll { get; init; }
        internal required Border Line { get; init; }
        internal required Func<IReadOnlyList<(string Folder, FrameworkElement Container, FrameworkElement Row)>> Rows { get; init; }
        internal required Pointer Pointer { get; init; }
        internal required Point Start { get; init; }
        internal bool Moving { get; set; }
        /// <summary>The place among the rows the folder would land before, or null where it would stay as it is.</summary>
        internal int? Index { get; set; }
    }

    private FolderDrag? _folderDrag;
    private long _folderDragEnded;
    private const double FolderDragThreshold = 6;
    private const double FolderDragEdge = 28;

    /// <summary>Whether a row drag ended a moment ago: the click its release might still raise on the row is not a click.</summary>
    private bool FolderDragJustEnded => Environment.TickCount64 - _folderDragEnded < 300;

    /// <summary>A panel's list with its drop line drawn over it, in the list's own coordinates, so the line scrolls with the rows.</summary>
    private FrameworkElement WithDropLine(StackPanel list, Border line)
    {
        line.Background = _theme.Accent;
        var overlay = new Canvas { IsHitTestVisible = false };
        overlay.Children.Add(line);
        var host = new Grid();
        host.Children.Add(list);
        host.Children.Add(overlay);
        return host;
    }

    /// <summary>Lets a listed folder's row be dragged to another place in the list; <paramref name="rows"/> gives the panel's folder rows in the list's order.</summary>
    private void MakeFolderRowDraggable(FrameworkElement row, string folder, StackPanel list, ScrollViewer scroll, Border line, Func<IReadOnlyList<(string Folder, FrameworkElement Container, FrameworkElement Row)>> rows)
    {
        // The row takes the pointer as it is pressed, not once it has moved: a quick drag left the row before its first move
        // reached it, and nothing happened (Chris, November 9, 2026: "It works sometimes but is not reliable"). The press is not
        // handled, so a click is still the row's click.
        row.PointerPressed += (_, args) =>
        {
            if (_folderDrag is { Moving: true } || _draggedPane is not null || args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
            var point = args.GetCurrentPoint(list);
            if (!point.Properties.IsLeftButtonPressed) return;
            if (args.OriginalSource is DependencyObject source && Ancestor<Button>(source) is not null) return;
            _folderDrag = new FolderDrag { Folder = folder, Row = row, List = list, Scroll = scroll, Line = line, Rows = rows, Pointer = args.Pointer, Start = point.Position };
            row.CapturePointer(args.Pointer);
        };
        row.PointerMoved += (_, args) =>
        {
            if (_folderDrag is not { } drag || !ReferenceEquals(drag.Row, row) || args.Pointer.PointerId != drag.Pointer.PointerId) return;
            var point = args.GetCurrentPoint(list);
            if (!drag.Moving)
            {
                if (!point.Properties.IsLeftButtonPressed) { _folderDrag = null; row.ReleasePointerCapture(args.Pointer); return; }
                if (Math.Abs(point.Position.X - drag.Start.X) < FolderDragThreshold && Math.Abs(point.Position.Y - drag.Start.Y) < FolderDragThreshold) return;
                if (Folders.Count < 2) { _folderDrag = null; row.ReleasePointerCapture(args.Pointer); return; }
                BeginFolderDrag(drag, args);
            }
            args.Handled = true;
            TrackFolderDrag(drag, args.GetCurrentPoint(list).Position.Y, args.GetCurrentPoint(scroll).Position.Y);
        };
        row.PointerReleased += (_, args) =>
        {
            if (_folderDrag is not { } drag || !ReferenceEquals(drag.Row, row)) return;
            if (!drag.Moving) { _folderDrag = null; row.ReleasePointerCapture(args.Pointer); return; }
            args.Handled = true;
            EndFolderDrag(drag, drop: true);
        };
        row.PointerCaptureLost += (_, _) =>
        {
            if (_folderDrag is not { } drag || !ReferenceEquals(drag.Row, row)) return;
            if (drag.Moving) EndFolderDrag(drag, drop: false); else _folderDrag = null;
        };
        row.PointerCanceled += (_, _) =>
        {
            if (_folderDrag is not { } drag || !ReferenceEquals(drag.Row, row)) return;
            if (drag.Moving) EndFolderDrag(drag, drop: false); else _folderDrag = null;
        };
    }

    /// <summary>Whether a row is being dragged: the panels' trees and session lists are put away meanwhile (<see cref="BeginFolderDrag"/>).</summary>
    private bool FolderDragging => _folderDrag is { Moving: true };

    /// <summary>
    /// The drag begins: the row dims, and every folder's tree in the Folders panel and every workspace's sessions in the Agent
    /// Workspaces panel are put away until the drop, folds unchanged, so the listed folders' rows stand together where they can be
    /// seen and passed; with a tree open, the row to pass could be a screen away, and the drop line out of view. The list is
    /// scrolled so the dragged row stays under the pointer as far as it can.
    /// </summary>
    private void BeginFolderDrag(FolderDrag drag, PointerRoutedEventArgs args)
    {
        drag.Moving = true;
        drag.Row.Opacity = 0.55;
        var grab = args.GetCurrentPoint(drag.Row).Position.Y;
        RefreshWorkspaceStates();
        drag.Scroll.UpdateLayout();
        var top = drag.Row.TransformToVisual(drag.List).TransformPoint(new Point(0, 0)).Y;
        drag.Scroll.ChangeView(null, Math.Max(0, top + grab - args.GetCurrentPoint(drag.Scroll).Position.Y), null, disableAnimation: true);
        drag.Scroll.UpdateLayout();
        Diagnostics.Log("folder drag: begun on " + Paths.Leaf(drag.Folder));
    }

    /// <summary>Esc ends a row drag with nothing moved, wherever the keyboard is, as it ends a pane drag.</summary>
    private void WatchFolderDrags() => _root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
    {
        if (args.Key != Windows.System.VirtualKey.Escape || _folderDrag is not { Moving: true } drag) return;
        args.Handled = true;
        EndFolderDrag(drag, drop: false);
    }), handledEventsToo: true);

    /// <summary>
    /// Where a row dragged to <paramref name="y"/> (in the list's pixels) would land, as the place among <paramref name="rows"/> it
    /// would go before: before the first row whose middle lies below the point, else after the last. Each row's own line decides,
    /// not the tree or the sessions under it. Null where the folder would stay where it is, just above or below its own row.
    /// </summary>
    private static int? FolderDropIndex(IReadOnlyList<(string Folder, FrameworkElement Container, FrameworkElement Row)> rows, StackPanel list, string folder, double y)
    {
        var index = 0;
        foreach (var (_, _, row) in rows)
        {
            if (row.ActualHeight <= 0) { index++; continue; }
            var top = row.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;
            if (y < top + row.ActualHeight / 2) break;
            index++;
        }
        var from = rows.ToList().FindIndex(entry => PinnedFolders.Same(entry.Folder, folder));
        return from < 0 || index == from || index == from + 1 ? null : index;
    }

    /// <summary>Verification: where a row of <paramref name="folder"/> dragged to <paramref name="y"/> would land in either panel.</summary>
    internal int? FolderDropIndexForCheck(bool workspacesPanel, string folder, double y)
        => workspacesPanel ? FolderDropIndex(WorkspaceDragRows(), _workspaceList, folder, y) : FolderDropIndex(FolderDragRows(), _folderList, folder, y);

    /// <summary>A step of the drag: the drop line where it would land, hidden where it would stay, and the panel scrolled near its edges.</summary>
    private void TrackFolderDrag(FolderDrag drag, double y, double viewportY)
    {
        var rows = drag.Rows();
        drag.Index = FolderDropIndex(rows, drag.List, drag.Folder, y);
        if (drag.Index is { } index && rows.Count > 0)
        {
            // In the gap above the row it would go before, or under the last row's container (its tree or sessions with it).
            var lineY = index < rows.Count
                ? rows[index].Container.TransformToVisual(drag.List).TransformPoint(new Point(0, 0)).Y - drag.List.Spacing / 2 - 1
                : rows[^1].Container.TransformToVisual(drag.List).TransformPoint(new Point(0, rows[^1].Container.ActualHeight)).Y;
            Canvas.SetTop(drag.Line, Math.Max(0, lineY));
            Canvas.SetLeft(drag.Line, 6);
            drag.Line.Width = Math.Max(0, drag.List.ActualWidth - 12);
            drag.Line.Visibility = Visibility.Visible;
        }
        else drag.Line.Visibility = Visibility.Collapsed;
        if (viewportY < FolderDragEdge) drag.Scroll.ChangeView(null, Math.Max(0, drag.Scroll.VerticalOffset - 16), null, disableAnimation: true);
        else if (viewportY > drag.Scroll.ViewportHeight - FolderDragEdge) drag.Scroll.ChangeView(null, drag.Scroll.VerticalOffset + 16, null, disableAnimation: true);
    }

    /// <summary>Ends the drag: the line hidden, the row as it was, and with <paramref name="drop"/> the folder moved where the line showed.</summary>
    private void EndFolderDrag(FolderDrag drag, bool drop)
    {
        // Cleared first: releasing the capture raises CaptureLost, which would end the drag a second time.
        _folderDrag = null;
        _folderDragEnded = Environment.TickCount64;
        drag.Line.Visibility = Visibility.Collapsed;
        drag.Row.Opacity = 1;
        drag.Row.ReleasePointerCapture(drag.Pointer);
        // The trees and session lists come back as their folds have them.
        RefreshWorkspaceStates();
        if (!drop || drag.Index is not { } index)
        {
            Diagnostics.Log("folder drag: ended on " + Paths.Leaf(drag.Folder) + " with nothing moved");
            return;
        }
        DropFolderAt(drag.Folder, index);
    }

    /// <summary>A dragged folder dropped before the row at <paramref name="index"/> (the row count for the end): its own row leaves first, so a place below it is one less.</summary>
    internal void DropFolderAt(string folder, int index)
    {
        var from = Folders.FindIndex(listed => PinnedFolders.Same(listed, folder));
        if (from >= 0) MoveFolderTo(folder, index > from ? index - 1 : index);
    }
}
