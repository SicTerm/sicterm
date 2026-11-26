using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Sic.App.Controls;

/// <summary>Where a drag over the canvas would drop: the pane under the pointer, the side of it nearest, and the half of it the drop would take.</summary>
internal sealed record PaneDrop(string Pane, PaneDirection Side, PaneRect Area);

/// <summary>
/// A tab's content: its panes and the dividers between them, laid out as <see cref="PaneLayout.Arrange"/> says for the tab's
/// split tree. Every pane is a direct child however deep its split, and a change of tree only arranges the children anew, so
/// a live terminal is moved in place and never reparented, which would unload it. Each divider is a <see cref="PaneSplitter"/>
/// that sets the size before it in pixels; the canvas turns that into the split's ratio, clamped to leave a minimum pane on
/// either side while the split has the room. A tab dragged over the canvas is shown where it would land (<see cref="ShowDrop"/>):
/// a translucent mark over the half of the pane under the pointer that the drop would take, with the dragged tab's name on it.
/// </summary>
internal sealed class PaneCanvas : Panel
{
    private readonly Theme _theme;
    private readonly Dictionary<string, PaneSplitter> _dividers = new(StringComparer.Ordinal);
    private PaneArrangement? _arrangement;
    // The drop mark: a fill and border over the area a drop would take, and a pill in its middle naming what is dragged. Never
    // hit-tested, so the drag goes on reaching the panes under it.
    private readonly Grid _dropMark = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _dropFill = new() { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2) };
    private readonly Border _dropPill = new() { CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 3, 12, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360 };
    private readonly TextBlock _dropLabel;
    // The mark's brushes, recolored for each showing rather than made anew at every step of a drag.
    private readonly SolidColorBrush _dropFillBrush = new(), _dropBorderBrush = new(), _dropPillBrush = new(), _dropInkBrush = new();
    private PaneRect _dropRect;

    /// <summary>The tree as it is now, the ratio of a divider being dragged included.</summary>
    internal PaneNode Tree { get; private set; }

    /// <summary>Once a divider's drag ends, with the split's path and its new ratio, already applied.</summary>
    internal event Action<string, double>? RatioChanged;

    internal PaneCanvas(Theme theme, PaneNode tree)
    {
        _theme = theme;
        Tree = tree;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        _dropLabel = _theme.BodyText("", 12, 600);
        _dropLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _dropLabel.Foreground = _dropInkBrush;
        _dropFill.Background = _dropFillBrush; _dropFill.BorderBrush = _dropBorderBrush; _dropPill.Background = _dropPillBrush;
        _dropPill.Child = _dropLabel;
        _dropMark.Children.Add(_dropFill);
        _dropMark.Children.Add(_dropPill);
        Children.Add(_dropMark);
    }

    /// <summary>The panes shown, in the tree's order.</summary>
    internal IEnumerable<SessionPane> Panes => Children.OfType<SessionPane>();

    /// <summary>The dividers shown, one per split, by path.</summary>
    internal IReadOnlyDictionary<string, PaneSplitter> Dividers => _dividers;

    /// <summary>Where everything was last laid out, for verification.</summary>
    internal PaneArrangement? Arrangement => _arrangement;

    /// <summary>The drop mark's place and text while it shows, for verification; null while it is hidden.</summary>
    internal (PaneRect Area, string Label, bool Allowed)? DropShown => _dropMark.Visibility == Visibility.Visible ? (_dropRect, _dropLabel.Text, _dropAllowed) : null;
    private bool _dropAllowed;

    /// <summary>
    /// Shows <paramref name="panes"/> tiled by <paramref name="tree"/>. A pane already shown stays in the children (its terminal
    /// is not unloaded); one no longer listed is taken out; the dividers are made afresh for the tree's splits. The drop mark
    /// stays the last child, over everything.
    /// </summary>
    internal void Show(PaneNode tree, IReadOnlyList<SessionPane> panes)
    {
        Tree = tree;
        foreach (var stale in Children.Where(child => child is PaneSplitter || (child is SessionPane pane && !panes.Contains(pane))).ToArray()) Children.Remove(stale);
        Children.Remove(_dropMark);
        foreach (var pane in panes) if (!Children.Contains(pane)) Children.Add(pane);
        _dividers.Clear();
        foreach (var (path, direction) in PaneLayout.Splits(tree))
        {
            var splitter = new PaneSplitter(rows: direction == PaneSplit.Rows, () => Before(path), size => Resize(path, size), _theme);
            splitter.Released += () => RatioChanged?.Invoke(path, RatioAt(path));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(splitter, direction == PaneSplit.Rows ? "Pane divider: drag to resize the panes above and below" : "Pane divider: drag to resize the panes on either side");
            _dividers[path] = splitter;
            Children.Add(splitter);
        }
        Children.Add(_dropMark);
        ForgetPlacements();
        InvalidateMeasure();
    }

    /// <summary>Takes a pane out of the children without retiling, for a pane on its way to another tab's canvas; a pane may have one parent at a time.</summary>
    internal void Detach(SessionPane pane) { Children.Remove(pane); ForgetPlacements(); }

    /// <summary>
    /// Where a drop at <paramref name="point"/> (in the canvas's own pixels) would land, from the last layout: the pane under the
    /// point, or the nearest when it is on a divider, the side of it the point is nearest (<see cref="PaneLayout.DropSide"/>),
    /// and the half of it the drop would take (<see cref="PaneLayout.DropArea"/>). Null before the canvas is laid out.
    /// </summary>
    internal PaneDrop? DropAt(Point point)
    {
        if (_arrangement is null || _arrangement.Panes.Count == 0) return null;
        PanePlacement? nearest = null;
        var least = double.PositiveInfinity;
        foreach (var placement in _arrangement.Panes)
        {
            var rect = placement.Rect;
            var dx = Math.Max(Math.Max(rect.X - point.X, 0), point.X - rect.Right);
            var dy = Math.Max(Math.Max(rect.Y - point.Y, 0), point.Y - rect.Bottom);
            var distance = dx * dx + dy * dy;
            if (distance >= least) continue;
            (nearest, least) = (placement, distance);
        }
        if (nearest is null) return null;
        var side = PaneLayout.DropSide(nearest.Rect, point.X, point.Y);
        return new PaneDrop(nearest.Pane, side, PaneLayout.DropArea(nearest.Rect, side, PaneSplitter.GrabSize));
    }

    /// <summary>
    /// Shows the drop mark over <paramref name="area"/> with <paramref name="label"/> on it: in the accent for a drop that may
    /// happen, in the danger color, the label saying why, for one that may not; an empty label leaves the mark alone to say it.
    /// </summary>
    internal void ShowDrop(PaneRect area, string label, bool allowed)
    {
        var color = allowed ? _theme.Accent.Color : _theme.Danger.Color;
        _dropFillBrush.Color = Theme.WithAlpha(color, allowed ? 0.22 : 0.16);
        _dropBorderBrush.Color = Theme.WithAlpha(color, 0.9);
        _dropPillBrush.Color = color;
        _dropInkBrush.Color = Theme.Ink(color);
        _dropLabel.Text = label;
        // No pill for a mark that says enough by itself (a tab over its own panes).
        _dropPill.Visibility = label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _dropAllowed = allowed;
        // Laid out again only when the mark's place changes; a step of the drag within the same half changes nothing but the text.
        var moved = _dropRect != area || _dropMark.Visibility != Visibility.Visible;
        _dropRect = area;
        _dropMark.Visibility = Visibility.Visible;
        if (moved) { ForgetPlacements(); InvalidateMeasure(); }
    }

    internal void HideDrop()
    {
        if (_dropMark.Visibility == Visibility.Collapsed) return;
        _dropMark.Visibility = Visibility.Collapsed;
        ForgetPlacements();
        InvalidateMeasure();
    }

    private DividerPlacement? Placed(string path) => _arrangement?.Dividers.FirstOrDefault(divider => divider.Path == path);

    private double Before(string path) => Placed(path)?.Before ?? 0;

    /// <summary>A drag step: the size asked for before the divider, as the split's ratio, clamped, and the panes laid out again.</summary>
    private void Resize(string path, double size)
    {
        if (Placed(path) is not { } divider || divider.Room <= 0) return;
        var minimum = divider.Direction == PaneSplit.Columns ? PaneLayout.MinPaneWidth : PaneLayout.MinPaneHeight;
        Tree = PaneLayout.WithRatio(Tree, path, PaneLayout.ClampRatio(size / divider.Room, divider.Room, minimum));
        ForgetPlacements();
        InvalidateMeasure();
    }

    /// <summary>A divider dragged to <paramref name="ratio"/> and released, as the pointer would do it: what the smoke test does in place of one.</summary>
    internal void DragTo(string path, double ratio)
    {
        Tree = PaneLayout.WithRatio(Tree, path, ratio);
        ForgetPlacements();
        InvalidateMeasure();
        RatioChanged?.Invoke(path, RatioAt(path));
    }

    private double RatioAt(string path)
    {
        var node = Tree;
        foreach (var step in path) node = step == '0' ? node.First! : node.Second!;
        return PaneLayout.ClampRatio(node.Ratio);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
        foreach (var (child, rect) in Placements(width, height)) child.Measure(new Size(rect.Width, rect.Height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (child, rect) in Placements(finalSize.Width, finalSize.Height)) child.Arrange(new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        return finalSize;
    }

    // The last placements and the size they were made for: the arrange that follows a measure of the same size reuses them, so
    // a layout pass (every frame of the panel's slide, every step of a divider drag) lays the tree out once rather than twice.
    private List<(UIElement Child, PaneRect Rect)>? _placements;
    private (double Width, double Height) _placedFor;

    /// <summary>Each child with its place for this size; the arrangement is kept for the dividers' drags and for verification. The drop mark takes its area while it shows and nothing otherwise.</summary>
    private List<(UIElement Child, PaneRect Rect)> Placements(double width, double height)
    {
        if (_placements is not null && _placedFor == (width, height)) return _placements;
        _arrangement = PaneLayout.Arrange(Tree, width, height, PaneSplitter.GrabSize);
        var panes = Panes.ToDictionary(pane => pane.Descriptor.Id, StringComparer.Ordinal);
        var placements = new List<(UIElement, PaneRect)>(_arrangement.Panes.Count + _arrangement.Dividers.Count + 1);
        foreach (var placement in _arrangement.Panes)
            if (panes.TryGetValue(placement.Pane, out var pane)) placements.Add((pane, placement.Rect));
        foreach (var placement in _arrangement.Dividers)
            if (_dividers.TryGetValue(placement.Path, out var divider)) placements.Add((divider, placement.Rect));
        placements.Add((_dropMark, _dropMark.Visibility == Visibility.Visible ? _dropRect : new PaneRect(0, 0, 0, 0)));
        _placements = placements; _placedFor = (width, height);
        return placements;
    }

    /// <summary>The placements are made again at the next pass: the tree, the children, a divider's ratio or the drop mark changed.</summary>
    private void ForgetPlacements() => _placements = null;
}
