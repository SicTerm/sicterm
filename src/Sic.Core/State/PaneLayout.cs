namespace Sic.Core.State;

/// <summary>The two ways a pane splits: into columns, the new pane to its right, or into rows, the new pane below it.</summary>
public enum PaneSplit { Columns, Rows }

/// <summary>The four ways the keyboard focus moves from a pane to the pane beside it (Alt+Arrow).</summary>
public enum PaneDirection { Left, Right, Up, Down }

/// <summary>Which sides of a pane another pane of its tab lies on: the directions Alt+Arrow moves the focus in from it.</summary>
public readonly record struct PaneNeighbors(bool Left, bool Right, bool Up, bool Down);

/// <summary>
/// One node of a tab's split tree, as it is saved: a pane, named by its <see cref="SavedPane.Id"/>, or a split of the node's
/// room between <see cref="First"/> (left or top) and <see cref="Second"/> (right or bottom). <see cref="Split"/> is
/// "columns" or "rows", and <see cref="Ratio"/> the first child's share of the room, clamped by
/// <see cref="PaneLayout.ClampRatio(double?)"/> when read.
/// </summary>
public sealed record PaneNode(string? Pane = null, string? Split = null, double? Ratio = null, PaneNode? First = null, PaneNode? Second = null)
{
    public bool IsLeaf => Pane is not null;
}

/// <summary>A rectangle inside a tab's content area, in the tab's own pixels.</summary>
public readonly record struct PaneRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Where a pane sits.</summary>
public sealed record PanePlacement(string Pane, PaneRect Rect);

/// <summary>
/// Where a divider lies and which way its split runs. <see cref="Path"/> names the split node from the root: "" for the root,
/// then one character per step, '0' into the first child and '1' into the second. For a drag, <see cref="Before"/> is the size
/// of what lies before the divider and <see cref="Room"/> the split's size less the divider, so a new ratio is (Before + the
/// drag) / Room.
/// </summary>
public sealed record DividerPlacement(string Path, PaneSplit Direction, PaneRect Rect, double Before, double Room);

/// <summary>Every pane's and every divider's place for one size of the tab's content area.</summary>
public sealed record PaneArrangement(IReadOnlyList<PanePlacement> Panes, IReadOnlyList<DividerPlacement> Dividers);

/// <summary>
/// How a tab tiles its panes: a binary tree of splits. Each split halves one pane's room and moves no other pane: a split into
/// columns opens the new pane to the right of the pane split, one into rows opens it below. Closing a pane hands its room to
/// the other side of its split. The tree is saved as <see cref="SavedTab.Layout"/>; a tab saved before there was one (up to
/// four panes in a fixed grid, with <see cref="SavedTab.SplitRatio"/> and <see cref="SavedTab.RowRatio"/>) is read as the
/// tree that tiles it the same way (<see cref="Legacy"/>). Which pane lies beside which, for Alt+Arrow, is <see cref="Neighbor"/>.
/// </summary>
public static class PaneLayout
{
    /// <summary>Panes a tab holds at most: enough for any real use, and a bound on the saved file.</summary>
    public const int MaxPanes = 16;
    /// <summary>
    /// The least and the most a split's first side may take: what a divider may be dragged to, and what a saved ratio is clamped
    /// to. A tenth, so a tab kept even (<see cref="Even"/>) can give one pane a tenth beside nine; it was 0.15 until October 21, 2026.
    /// </summary>
    public const double MinRatio = 0.1, MaxRatio = 0.9;
    /// <summary>The narrowest and the shortest a pane is laid out at while its split has the room, and the least it may be split down to.</summary>
    public const double MinPaneWidth = 160, MinPaneHeight = 120;
    private const string ColumnsWord = "columns", RowsWord = "rows";
    // Facing edges on the unit square (see Neighbor) are sums of the same ratios taken in different orders, so they may differ
    // in the last bits. No pane a person can make is anywhere near a billionth of its tab.
    private const double Tolerance = 1e-9;

    public static PaneNode Leaf(string pane) => new(Pane: pane);

    public static PaneNode SplitNode(PaneSplit direction, double ratio, PaneNode first, PaneNode second) =>
        new(Split: Word(direction), Ratio: ratio, First: first, Second: second);

    /// <summary>The split's direction; a leaf, or a node with a word that is neither "columns" nor "rows", is not a split.</summary>
    public static PaneSplit? DirectionOf(PaneNode node) => node.Split switch
    {
        _ when node.IsLeaf => null,
        { } word when string.Equals(word, ColumnsWord, StringComparison.OrdinalIgnoreCase) => PaneSplit.Columns,
        { } word when string.Equals(word, RowsWord, StringComparison.OrdinalIgnoreCase) => PaneSplit.Rows,
        _ => null
    };

    private static string Word(PaneSplit direction) => direction == PaneSplit.Columns ? ColumnsWord : RowsWord;

    /// <summary>The panes in the tree's order: first child before second, depth first. The order a tab's panes are listed in.</summary>
    public static IReadOnlyList<string> Leaves(PaneNode tree)
    {
        var leaves = new List<string>();
        Walk(tree, leaves);
        return leaves;

        static void Walk(PaneNode node, List<string> into)
        {
            if (node.IsLeaf) { into.Add(node.Pane!); return; }
            Walk(node.First!, into);
            Walk(node.Second!, into);
        }
    }

    /// <summary>Every split in the tree, by path and direction, in the tree's order.</summary>
    public static IReadOnlyList<(string Path, PaneSplit Direction)> Splits(PaneNode tree)
    {
        var splits = new List<(string, PaneSplit)>();
        Walk(tree, "", splits);
        return splits;

        static void Walk(PaneNode node, string path, List<(string, PaneSplit)> into)
        {
            if (node.IsLeaf) return;
            into.Add((path, DirectionOf(node)!.Value));
            Walk(node.First!, path + "0", into);
            Walk(node.Second!, path + "1", into);
        }
    }

    /// <summary>
    /// Splits <paramref name="pane"/>'s room evenly with <paramref name="newPane"/>, which opens to its right (columns) or
    /// below it (rows). Every other pane keeps its place.
    /// </summary>
    public static PaneNode Split(PaneNode tree, string pane, PaneSplit direction, string newPane)
    {
        var leaves = Leaves(tree);
        if (!leaves.Contains(pane)) throw new ArgumentException("The tab holds no pane " + pane + ".", nameof(pane));
        if (leaves.Contains(newPane)) throw new ArgumentException("The tab already holds pane " + newPane + ".", nameof(newPane));
        if (leaves.Count >= MaxPanes) throw new InvalidOperationException($"A tab holds up to {MaxPanes} panes.");
        return Replace(tree, pane, leaf => SplitNode(direction, 0.5, leaf, Leaf(newPane)));
    }

    /// <summary>The way a pane splits to make room on that side: columns for left and right, rows for up and down.</summary>
    public static PaneSplit SplitOf(PaneDirection side) => side is PaneDirection.Left or PaneDirection.Right ? PaneSplit.Columns : PaneSplit.Rows;

    /// <summary>
    /// Puts a whole tree, a tab's panes as they are tiled, into <paramref name="pane"/>'s room on <paramref name="side"/> of
    /// it: the pane's leaf becomes an even split of the two, the subtree first for left and up, the pane first for right and
    /// down, so the dropped panes keep their own tiling and every other pane keeps its place. Refused when the pane is not in
    /// the tree, a pane of the subtree already is, or the two together pass <see cref="MaxPanes"/>.
    /// </summary>
    public static PaneNode Insert(PaneNode tree, string pane, PaneDirection side, PaneNode subtree)
    {
        var leaves = Leaves(tree);
        if (!leaves.Contains(pane)) throw new ArgumentException("The tab holds no pane " + pane + ".", nameof(pane));
        var coming = Leaves(subtree);
        if (coming.Any(leaves.Contains)) throw new ArgumentException("The tab already holds a pane of the tree being put in.", nameof(subtree));
        if (leaves.Count + coming.Count > MaxPanes) throw new InvalidOperationException($"A tab holds up to {MaxPanes} panes.");
        var first = side is PaneDirection.Left or PaneDirection.Up;
        return Replace(tree, pane, leaf => SplitNode(SplitOf(side), 0.5, first ? subtree : leaf, first ? leaf : subtree));
    }

    /// <summary>
    /// The side of <paramref name="rect"/> a point is nearest, each distance taken as a share of the rectangle's size that way,
    /// so the four sides meet at the centre whatever the rectangle's shape: the side a drop at that point lands on. A point at
    /// the exact centre, or outside, goes down, the way a tab is most often dragged.
    /// </summary>
    public static PaneDirection DropSide(PaneRect rect, double x, double y)
    {
        var width = Math.Max(rect.Width, 1e-9);
        var height = Math.Max(rect.Height, 1e-9);
        var left = (x - rect.X) / width;
        var right = (rect.Right - x) / width;
        var up = (y - rect.Y) / height;
        var down = (rect.Bottom - y) / height;
        var least = Math.Min(Math.Min(left, right), Math.Min(up, down));
        if (down <= least + Tolerance) return PaneDirection.Down;
        if (right <= least + Tolerance) return PaneDirection.Right;
        if (up <= least + Tolerance) return PaneDirection.Up;
        return PaneDirection.Left;
    }

    /// <summary>
    /// The part of <paramref name="rect"/> a drop on <paramref name="side"/> hands to what is dropped: the half on that side,
    /// less the divider, as <see cref="Insert"/> then tiles it. The preview a drag shows before it is released.
    /// </summary>
    public static PaneRect DropArea(PaneRect rect, PaneDirection side, double divider)
    {
        var columns = SplitOf(side) == PaneSplit.Columns;
        var extent = columns ? rect.Width : rect.Height;
        var room = Math.Max(0, extent - divider);
        var before = room / 2;
        var bar = Math.Min(divider, Math.Max(0, extent - before));
        return side switch
        {
            PaneDirection.Left => new PaneRect(rect.X, rect.Y, before, rect.Height),
            PaneDirection.Right => new PaneRect(rect.X + before + bar, rect.Y, room - before, rect.Height),
            PaneDirection.Up => new PaneRect(rect.X, rect.Y, rect.Width, before),
            _ => new PaneRect(rect.X, rect.Y + before + bar, rect.Width, room - before)
        };
    }

    /// <summary>Takes <paramref name="pane"/> out, handing its room to the other side of its split. Null when it was the only pane.</summary>
    public static PaneNode? Remove(PaneNode tree, string pane)
    {
        if (!Leaves(tree).Contains(pane)) throw new ArgumentException("The tab holds no pane " + pane + ".", nameof(pane));
        return Prune(tree);

        PaneNode? Prune(PaneNode node)
        {
            if (node.IsLeaf) return node.Pane == pane ? null : node;
            var first = Prune(node.First!);
            var second = Prune(node.Second!);
            if (first is null) return second;
            if (second is null) return first;
            return node with { First = first, Second = second };
        }
    }

    /// <summary>The tree with the split at <paramref name="path"/> given <paramref name="ratio"/>, clamped.</summary>
    public static PaneNode WithRatio(PaneNode tree, string path, double ratio)
    {
        if (tree.IsLeaf) throw new ArgumentException("No split lies at path \"" + path + "\".", nameof(path));
        if (path.Length == 0) return tree with { Ratio = ClampRatio(ratio) };
        return path[0] switch
        {
            '0' => tree with { First = WithRatio(tree.First!, path[1..], ratio) },
            '1' => tree with { Second = WithRatio(tree.Second!, path[1..], ratio) },
            _ => throw new ArgumentException("A path is made of '0' and '1' only: \"" + path + "\".", nameof(path))
        };
    }

    /// <summary>
    /// The tree with every pane given the same share of the tab: each split's ratio is the share of the tree's panes on its first
    /// side, so a pane beside a column of two takes a third and the column two thirds, and every pane an equal area (the dividers
    /// aside). What a tab that keeps its panes even (<see cref="SavedTab.KeepEven"/>) is retiled to after a split or a close.
    /// </summary>
    public static PaneNode Even(PaneNode tree)
    {
        if (tree.IsLeaf) return tree;
        var first = Leaves(tree.First!).Count;
        var second = Leaves(tree.Second!).Count;
        return tree with { Ratio = ClampRatio((double)first / (first + second)), First = Even(tree.First!), Second = Even(tree.Second!) };
    }

    /// <summary>
    /// Whether no divider of the tree has been dragged: every split is still at the half a split starts at. A tab saved before
    /// there was a choice to keep its panes even is taken to want that when this holds, and to keep its own ratios otherwise.
    /// </summary>
    public static bool Untouched(PaneNode tree) =>
        tree.IsLeaf || (ClampRatio(tree.Ratio) == 0.5 && Untouched(tree.First!) && Untouched(tree.Second!));

    private static PaneNode Replace(PaneNode node, string pane, Func<PaneNode, PaneNode> with)
    {
        if (node.IsLeaf) return node.Pane == pane ? with(node) : node;
        return node with { First = Replace(node.First!, pane, with), Second = Replace(node.Second!, pane, with) };
    }

    /// <summary>
    /// The tree a tab saved before there were trees tiles as: one pane; two side by side; three with the right-hand pane split
    /// into rows; four as a 2×2 grid. The pane order is the one that layout listed: top-left, top-right, bottom-right,
    /// bottom-left. <paramref name="splitRatio"/> is the left column's share and <paramref name="rowRatio"/> the top row's.
    /// </summary>
    public static PaneNode Legacy(IReadOnlyList<string> panes, double? splitRatio, double? rowRatio)
    {
        var columns = ClampRatio(splitRatio);
        var rows = ClampRatio(rowRatio);
        return panes.Count switch
        {
            1 => Leaf(panes[0]),
            2 => SplitNode(PaneSplit.Columns, columns, Leaf(panes[0]), Leaf(panes[1])),
            3 => SplitNode(PaneSplit.Columns, columns, Leaf(panes[0]), SplitNode(PaneSplit.Rows, rows, Leaf(panes[1]), Leaf(panes[2]))),
            4 => SplitNode(PaneSplit.Rows, rows,
                SplitNode(PaneSplit.Columns, columns, Leaf(panes[0]), Leaf(panes[1])),
                SplitNode(PaneSplit.Columns, columns, Leaf(panes[3]), Leaf(panes[2]))),
            _ => throw new ArgumentOutOfRangeException(nameof(panes), panes.Count, "A tab saved without a layout tree held one to four panes.")
        };
    }

    /// <summary>
    /// What is wrong with a saved tree for these panes, or null when nothing is: every node is a pane or a two-sided split with
    /// a known direction, and the leaves are exactly the panes, each once.
    /// </summary>
    public static string? Check(PaneNode tree, IReadOnlyCollection<string> panes)
    {
        var leaves = new List<string>();
        if (Fault(tree, leaves) is { } fault) return fault;
        if (leaves.Count != leaves.Distinct(StringComparer.Ordinal).Count()) return "The saved layout lists a pane twice.";
        if (!leaves.ToHashSet(StringComparer.Ordinal).SetEquals(panes)) return "The saved layout does not match the tab's panes.";
        return null;

        static string? Fault(PaneNode node, List<string> leaves)
        {
            if (node.IsLeaf)
            {
                if (node.Split is not null || node.First is not null || node.Second is not null) return "A pane in the saved layout is also a split.";
                leaves.Add(node.Pane!);
                return null;
            }
            if (DirectionOf(node) is null) return "A split in the saved layout has no known direction.";
            if (node.First is null || node.Second is null) return "A split in the saved layout is missing a side.";
            return Fault(node.First, leaves) ?? Fault(node.Second, leaves);
        }
    }

    /// <summary>
    /// The tab's tree: the saved one, checked against the tab's panes, or, for a tab saved before there were trees, the one
    /// that tiles its panes as that layout did. An unreadable tree is a fault in the saved file, not something to guess at.
    /// </summary>
    public static PaneNode TreeOf(SavedTab tab)
    {
        var ids = tab.Panes.Select(pane => pane.Id).ToList();
        if (ids.Count is < 1 or > MaxPanes) throw new InvalidDataException($"A saved tab must contain one to {MaxPanes} panes.");
        if (ids.Count != ids.Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("A saved tab lists a pane twice.");
        if (tab.Layout is null)
        {
            if (ids.Count > 4) throw new InvalidDataException("A saved tab of more than four panes needs a layout.");
            return Legacy(ids, tab.SplitRatio, tab.RowRatio);
        }
        return Check(tab.Layout, ids) is { } fault ? throw new InvalidDataException(fault) : tab.Layout;
    }

    /// <summary>A saved ratio kept inside the range the dividers allow; missing or unreadable values split evenly.</summary>
    public static double ClampRatio(double? ratio) => ratio is { } value && double.IsFinite(value) ? Math.Clamp(value, MinRatio, MaxRatio) : 0.5;

    /// <summary>
    /// A dragged ratio: inside the range, and, while the split's room allows, leaving at least the minimum on either side.
    /// <paramref name="room"/> is the split's size less the divider and <paramref name="minimum"/> the least a side may have.
    /// </summary>
    public static double ClampRatio(double ratio, double room, double minimum)
    {
        var clamped = ClampRatio(ratio);
        if (room < 2 * minimum || minimum <= 0) return clamped;
        return Math.Clamp(clamped, Math.Max(MinRatio, minimum / room), Math.Min(MaxRatio, 1 - minimum / room));
    }

    /// <summary>The least a pane may have along a direction for it to be split that way: two minimum panes and the divider between them.</summary>
    public static double LeastToSplit(PaneSplit direction, double divider) => 2 * (direction == PaneSplit.Columns ? MinPaneWidth : MinPaneHeight) + divider;

    /// <summary>
    /// Whether a pane of this size may be split this way. A size not yet known (zero, before the pane is laid out) does not
    /// refuse: the pane's room is decided by its tab, not by what is known of it.
    /// </summary>
    public static bool CanSplit(PaneSplit direction, double width, double height, double divider)
    {
        var extent = direction == PaneSplit.Columns ? width : height;
        return extent <= 0 || extent >= LeastToSplit(direction, divider);
    }

    /// <summary>The way a pane splits when no direction is named: along its longer side, so the two halves stay as square as they can.</summary>
    public static PaneSplit AutoSplit(double width, double height) => width >= height ? PaneSplit.Columns : PaneSplit.Rows;

    /// <summary>The pane with the most room in an arrangement, the first in the tree's order among equals: where a tab consolidated into another goes.</summary>
    public static PanePlacement Largest(PaneArrangement arrangement)
    {
        if (arrangement.Panes.Count == 0) throw new ArgumentException("The arrangement has no panes.", nameof(arrangement));
        PanePlacement? best = null;
        foreach (var placement in arrangement.Panes)
            if (best is null || placement.Rect.Width * placement.Rect.Height > best.Rect.Width * best.Rect.Height) best = placement;
        return best!;
    }

    /// <summary>
    /// The way a pane of this size is split when nothing names one and both ways are tried: along its longer side when that has
    /// the room (<see cref="CanSplit"/>), else the other way when that has, else null.
    /// </summary>
    public static PaneSplit? SplitThatFits(double width, double height, double divider)
    {
        var preferred = AutoSplit(width, height);
        var other = preferred == PaneSplit.Columns ? PaneSplit.Rows : PaneSplit.Columns;
        return CanSplit(preferred, width, height, divider) ? preferred : CanSplit(other, width, height, divider) ? other : null;
    }

    /// <summary>
    /// Lays the tree out in a content area <paramref name="width"/> by <paramref name="height"/>, with a divider of
    /// <paramref name="divider"/> pixels at every split. A split's first side takes its ratio of the room left by the
    /// divider, kept at or above the minimum pane size on both sides while the room holds two minimums; in less room the ratio
    /// is taken as it is, and a split whose room is gone has nothing to give either side.
    /// </summary>
    public static PaneArrangement Arrange(PaneNode tree, double width, double height, double divider, double minWidth = MinPaneWidth, double minHeight = MinPaneHeight)
    {
        var panes = new List<PanePlacement>();
        var dividers = new List<DividerPlacement>();
        Place(tree, new PaneRect(0, 0, Math.Max(0, width), Math.Max(0, height)), "");
        return new(panes, dividers);

        void Place(PaneNode node, PaneRect rect, string path)
        {
            if (node.IsLeaf) { panes.Add(new(node.Pane!, rect)); return; }
            var direction = DirectionOf(node) ?? throw new InvalidDataException("A split in the layout has no known direction.");
            var columns = direction == PaneSplit.Columns;
            var extent = columns ? rect.Width : rect.Height;
            var room = Math.Max(0, extent - divider);
            var minimum = columns ? minWidth : minHeight;
            var before = room * ClampRatio(node.Ratio);
            if (room >= 2 * minimum) before = Math.Clamp(before, minimum, room - minimum);
            var after = room - before;
            var bar = Math.Min(divider, Math.Max(0, extent - before));
            if (columns)
            {
                dividers.Add(new(path, direction, new PaneRect(rect.X + before, rect.Y, bar, rect.Height), before, room));
                Place(node.First!, new PaneRect(rect.X, rect.Y, before, rect.Height), path + "0");
                Place(node.Second!, new PaneRect(rect.X + before + bar, rect.Y, after, rect.Height), path + "1");
            }
            else
            {
                dividers.Add(new(path, direction, new PaneRect(rect.X, rect.Y + before, rect.Width, bar), before, room));
                Place(node.First!, new PaneRect(rect.X, rect.Y, rect.Width, before), path + "0");
                Place(node.Second!, new PaneRect(rect.X, rect.Y + before + bar, rect.Width, after), path + "1");
            }
        }
    }

    /// <summary>
    /// The pane Alt+Arrow moves the focus to from <paramref name="pane"/>. Of the panes across the divider on that side (those
    /// sharing some of its edge), it is the one focused last by <paramref name="lastFocused"/> (a running count, 0 for never),
    /// so going there and back returns to the same pane, as tmux does; then the one sharing the most of the edge; then the first
    /// in the tree's order. Null when the pane is at the tab's edge that way. Worked out on the tree's own proportions (each
    /// split at its ratio, no dividers, no minimums), so every pane has room, facing edges meet exactly, and the answer is the
    /// same at any window size.
    /// </summary>
    public static string? Neighbor(PaneNode tree, string pane, PaneDirection direction, Func<string, long>? lastFocused = null)
    {
        var placements = Proportions(tree);
        return Beside(placements, RectOf(placements, pane), pane, direction, lastFocused);
    }

    /// <summary>The sides of <paramref name="pane"/> that another pane lies on, as <see cref="Neighbor"/> finds them; none in a tab of one pane.</summary>
    public static PaneNeighbors NeighborsOf(PaneNode tree, string pane)
    {
        var placements = Proportions(tree);
        var from = RectOf(placements, pane);
        bool Has(PaneDirection direction) => Beside(placements, from, pane, direction, null) is not null;
        return new(Has(PaneDirection.Left), Has(PaneDirection.Right), Has(PaneDirection.Up), Has(PaneDirection.Down));
    }

    private static IReadOnlyList<PanePlacement> Proportions(PaneNode tree) => Arrange(tree, 1, 1, divider: 0, minWidth: 0, minHeight: 0).Panes;

    private static PaneRect RectOf(IReadOnlyList<PanePlacement> placements, string pane) =>
        placements.FirstOrDefault(placement => placement.Pane == pane)?.Rect ?? throw new ArgumentException("The tab holds no pane " + pane + ".", nameof(pane));

    private static string? Beside(IReadOnlyList<PanePlacement> placements, PaneRect from, string pane, PaneDirection direction, Func<string, long>? lastFocused)
    {
        var across = direction is PaneDirection.Left or PaneDirection.Right;
        string? best = null;
        double bestShared = 0;
        long bestFocused = 0;
        foreach (var (id, rect) in placements)
        {
            var (edge, facing) = direction switch
            {
                PaneDirection.Left => (from.X, rect.Right),
                PaneDirection.Right => (from.Right, rect.X),
                PaneDirection.Up => (from.Y, rect.Bottom),
                _ => (from.Bottom, rect.Y)
            };
            var shared = across ? Math.Min(from.Bottom, rect.Bottom) - Math.Max(from.Y, rect.Y) : Math.Min(from.Right, rect.Right) - Math.Max(from.X, rect.X);
            if (id == pane || Math.Abs(edge - facing) > Tolerance || shared <= Tolerance) continue;
            var focused = lastFocused?.Invoke(id) ?? 0;
            // The placements come in the tree's order, so a tie keeps the pane found first.
            if (best is not null && (focused < bestFocused || (focused == bestFocused && shared <= bestShared + Tolerance))) continue;
            (best, bestShared, bestFocused) = (id, shared, focused);
        }
        return best;
    }
}
