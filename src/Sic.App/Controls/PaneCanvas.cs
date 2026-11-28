using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
    // The zoom (Sic.Core.Appearance.PaneZoom): the zoomed pane's id and its card, in the canvas's own pixels, which may reach past
    // its edges over the window's body; and the dim over the other panes, which takes a press so it never reaches their terminals.
    private readonly Border _dim = new() { Visibility = Visibility.Collapsed };
    private string? _zoomed;
    private PaneRect _zoomCard;
    // Over the tiled panes and the dividers: the dim, a pane on its way back from the card, the zoomed pane, and the drop mark above them all.
    private const int DimLayer = 10, LeavingLayer = 11, ZoomedLayer = 12, DropLayer = 13;
    // The zoom's motion while it runs: the panes it moves, each laid out at its new place at once and drawn from its old one by a
    // render transform that eases away (the terminal is resized once, not at every frame), and the dim fading in or out.
    private Storyboard? _zoomMotion;
    private readonly List<SessionPane> _moving = [];
    private TaskCompletionSource? _zoomMoved;

    /// <summary>How long the card takes to grow out of the pane's place or to shrink back into it. The smoke slows it to watch it.</summary>
    internal static TimeSpan ZoomMotion { get; set; } = TimeSpan.FromMilliseconds(180);

    /// <summary>Whether the zoom's motion is running, for verification.</summary>
    internal bool ZoomMoving => _zoomMotion is not null;

    /// <summary>A press on the dim behind the zoomed pane, with the pane under it (the nearest, on a divider), already found.</summary>
    internal event Action<string>? DimPressed;

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
        Canvas.SetZIndex(_dropMark, DropLayer);
        Children.Add(_dropMark);
        _dim.Background = new SolidColorBrush(Theme.WithAlpha(Microsoft.UI.Colors.Black, 0.55));
        Canvas.SetZIndex(_dim, DimLayer);
        _dim.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            if (NearestPane(args.GetCurrentPoint(this).Position) is { } pressed) DimPressed?.Invoke(pressed.Pane);
        };
        Children.Add(_dim);
    }

    /// <summary>The pane shown as a card over the others, by id; null while none is.</summary>
    internal string? Zoomed => _zoomed;

    /// <summary>Where the zoomed pane's card is, in the canvas's pixels; empty while none is zoomed.</summary>
    internal PaneRect ZoomCard => _zoomed is null ? default : _zoomCard;

    /// <summary>Whether the dim behind the zoomed pane shows, for verification.</summary>
    internal bool Dimmed => _dim.Visibility == Visibility.Visible;

    /// <summary>
    /// Shows the pane <paramref name="pane"/> as a card at <paramref name="card"/> (the canvas's pixels; it may reach past the
    /// canvas's edges), over the other panes, which stay tiled under a dim; null puts it back in its place. Only the arrangement
    /// changes: the pane stays a child of the canvas, so its terminal is resized and never reloaded. With <paramref name="animate"/>
    /// the card grows out of the pane's place, or shrinks back into it, and the dim fades, over <see cref="ZoomMotion"/>; a zoom
    /// moved to another pane does both at once. The task ends when the motion has, at once without one. A new card for the same
    /// pane (the window resized) is never animated, and leaves a motion under way running.
    /// </summary>
    internal Task Zoom(string? pane, PaneRect card, bool animate = false)
    {
        if (_zoomed == pane && (pane is null || _zoomCard == card)) return _zoomMoved?.Task ?? Task.CompletedTask;
        var leaving = _zoomed is not null && _zoomed != pane ? PaneById(_zoomed) : null;
        var arriving = pane is not null && pane != _zoomed ? PaneById(pane) : null;
        var wasZoomed = _zoomed is not null;
        // Where each moving pane is drawn now, before any motion under way is cut short: its slot, or the card it was on.
        var leavingFrom = leaving is null ? default : Slot(leaving);
        var arrivingFrom = arriving is null ? default : Slot(arriving);
        if (leaving is not null || arriving is not null) FinishZoomMotion();
        if (leaving is not null) Canvas.SetZIndex(leaving, 0);
        _zoomed = pane;
        _zoomCard = card;
        if (pane is not null && PaneById(pane) is { } zoomed) Canvas.SetZIndex(zoomed, ZoomedLayer);
        var moves = animate && ZoomMotion > TimeSpan.Zero && (leaving is not null || arriving is not null);
        // Zooming out, the dim stays to fade, and no longer takes a press.
        _dim.Visibility = pane is not null || (moves && wasZoomed) ? Visibility.Visible : Visibility.Collapsed;
        _dim.IsHitTestVisible = pane is not null;
        ForgetPlacements();
        InvalidateMeasure();
        if (!moves) return Task.CompletedTask;

        var motion = new Storyboard();
        if (arriving is not null) Flip(motion, arriving, arrivingFrom, card);
        if (leaving is not null && _arrangement?.Panes.FirstOrDefault(placement => placement.Pane == leaving.Descriptor.Id) is { } home)
        {
            // Above the dim until it is back in its place, under a pane taking the zoom from it.
            Canvas.SetZIndex(leaving, LeavingLayer);
            Flip(motion, leaving, leavingFrom, home.Rect);
        }
        if (wasZoomed != (pane is not null))
        {
            var fade = new DoubleAnimation { From = wasZoomed ? 1 : 0, To = wasZoomed ? 0 : 1, Duration = new Duration(ZoomMotion), EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 } };
            Storyboard.SetTarget(fade, _dim);
            Storyboard.SetTargetProperty(fade, "Opacity");
            motion.Children.Add(fade);
        }
        if (motion.Children.Count == 0) { FinishZoomMotion(); return Task.CompletedTask; }
        var moved = _zoomMoved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _zoomMotion = motion;
        motion.Completed += (_, _) => { if (ReferenceEquals(_zoomMotion, motion)) FinishZoomMotion(); };
        motion.Begin();
        _ = FinishStalledMotionAsync(motion);
        return moved.Task;
    }

    private SessionPane? PaneById(string id) => Panes.FirstOrDefault(item => item.Descriptor.Id == id);

    /// <summary>Where the pane was last laid out, in the canvas's pixels.</summary>
    private static PaneRect Slot(FrameworkElement element)
    {
        var slot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(element);
        return new PaneRect(slot.X, slot.Y, slot.Width, slot.Height);
    }

    /// <summary>
    /// The pane laid out at <paramref name="to"/> drawn at <paramref name="from"/> by a scale and a shift from its top-left corner,
    /// both easing to nothing in the panel's curve, so it is seen to grow or shrink from one place to the other.
    /// </summary>
    private void Flip(Storyboard motion, SessionPane pane, PaneRect from, PaneRect to)
    {
        if (from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0 || from == to) return;
        var transform = new CompositeTransform { ScaleX = from.Width / to.Width, ScaleY = from.Height / to.Height, TranslateX = from.X - to.X, TranslateY = from.Y - to.Y };
        pane.RenderTransformOrigin = new Point(0, 0);
        pane.RenderTransform = transform;
        _moving.Add(pane);
        foreach (var (property, value) in new[] { ("ScaleX", 1.0), ("ScaleY", 1.0), ("TranslateX", 0.0), ("TranslateY", 0.0) })
        {
            var animation = new DoubleAnimation { To = value, Duration = new Duration(ZoomMotion), EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 } };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            motion.Children.Add(animation);
        }
    }

    /// <summary>The compositor does not tick while the window is hidden; a motion that never completes must not leave a pane drawn part-way.</summary>
    private async Task FinishStalledMotionAsync(Storyboard motion)
    {
        await Task.Delay(ZoomMotion + TimeSpan.FromSeconds(1));
        if (ReferenceEquals(_zoomMotion, motion)) FinishZoomMotion();
    }

    /// <summary>Ends the zoom's motion where it was going: every moved pane drawn where it is laid out, on its own layer, and the dim at rest.</summary>
    private void FinishZoomMotion()
    {
        if (_zoomMotion is { } motion) { _zoomMotion = null; motion.Stop(); }
        foreach (var pane in _moving)
        {
            pane.RenderTransform = null;
            if (pane.Descriptor.Id != _zoomed) Canvas.SetZIndex(pane, 0);
        }
        _moving.Clear();
        _dim.Opacity = 1;
        if (_zoomed is null && _dim.Visibility == Visibility.Visible)
        {
            _dim.Visibility = Visibility.Collapsed;
            ForgetPlacements();
            InvalidateMeasure();
        }
        _zoomMoved?.TrySetResult();
        _zoomMoved = null;
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
        // A new tiling ends a zoom's motion where it was going.
        FinishZoomMotion();
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
        // A zoomed pane that has left the tab takes the zoom with it.
        if (_zoomed is not null && !panes.Any(pane => pane.Descriptor.Id == _zoomed)) { _zoomed = null; _dim.Visibility = Visibility.Collapsed; }
        // A pane moved in from another tab may still carry the layer it was zoomed on there.
        foreach (var pane in panes) Canvas.SetZIndex(pane, pane.Descriptor.Id == _zoomed ? ZoomedLayer : 0);
        ForgetPlacements();
        InvalidateMeasure();
    }

    /// <summary>Takes a pane out of the children without retiling, for a pane on its way to another tab's canvas; a pane may have one parent at a time.</summary>
    internal void Detach(SessionPane pane)
    {
        FinishZoomMotion();
        Children.Remove(pane);
        Canvas.SetZIndex(pane, 0);
        if (pane.Descriptor.Id == _zoomed) { _zoomed = null; _dim.Visibility = Visibility.Collapsed; }
        ForgetPlacements();
    }

    /// <summary>
    /// Where a drop at <paramref name="point"/> (in the canvas's own pixels) would land, from the last layout: the pane under the
    /// point, or the nearest when it is on a divider, the side of it the point is nearest (<see cref="PaneLayout.DropSide"/>),
    /// and the half of it the drop would take (<see cref="PaneLayout.DropArea"/>). Null before the canvas is laid out.
    /// </summary>
    internal PaneDrop? DropAt(Point point)
    {
        if (NearestPane(point) is not { } nearest) return null;
        var side = PaneLayout.DropSide(nearest.Rect, point.X, point.Y);
        return new PaneDrop(nearest.Pane, side, PaneLayout.DropArea(nearest.Rect, side, PaneSplitter.GrabSize));
    }

    /// <summary>The pane under <paramref name="point"/> (the canvas's pixels) as last tiled, or the nearest when the point is on a divider; null before the canvas is laid out.</summary>
    private PanePlacement? NearestPane(Point point)
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
        return nearest;
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
        // The zoomed pane at its card; the others where the tree tiles them, under the dim, which covers the canvas.
        foreach (var placement in _arrangement.Panes)
            if (panes.TryGetValue(placement.Pane, out var pane)) placements.Add((pane, placement.Pane == _zoomed ? _zoomCard : placement.Rect));
        foreach (var placement in _arrangement.Dividers)
            if (_dividers.TryGetValue(placement.Path, out var divider)) placements.Add((divider, placement.Rect));
        placements.Add((_dim, _dim.Visibility == Visibility.Visible ? new PaneRect(0, 0, width, height) : new PaneRect(0, 0, 0, 0)));
        placements.Add((_dropMark, _dropMark.Visibility == Visibility.Visible ? _dropRect : new PaneRect(0, 0, 0, 0)));
        _placements = placements; _placedFor = (width, height);
        return placements;
    }

    /// <summary>The placements are made again at the next pass: the tree, the children, a divider's ratio or the drop mark changed.</summary>
    private void ForgetPlacements() => _placements = null;
}
