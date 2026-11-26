using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PaneLayoutTests
{
    private const double Divider = 8;

    private static PaneNode Cols(double ratio, PaneNode first, PaneNode second) => PaneLayout.SplitNode(PaneSplit.Columns, ratio, first, second);
    private static PaneNode Rows(double ratio, PaneNode first, PaneNode second) => PaneLayout.SplitNode(PaneSplit.Rows, ratio, first, second);
    private static PaneNode Leaf(string pane) => PaneLayout.Leaf(pane);

    private static PaneRect RectOf(PaneArrangement arrangement, string pane) => arrangement.Panes.Single(placement => placement.Pane == pane).Rect;

    private static bool Overlap(PaneRect a, PaneRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    /// <summary>Panes and dividers together cover the area exactly once: no overlap, and their areas add up to the whole.</summary>
    private static void AssertTiled(PaneArrangement arrangement, double width, double height)
    {
        var rects = arrangement.Panes.Select(placement => placement.Rect).Concat(arrangement.Dividers.Select(divider => divider.Rect)).ToList();
        for (var a = 0; a < rects.Count; a++)
            for (var b = a + 1; b < rects.Count; b++)
                Assert.False(Overlap(rects[a], rects[b]), $"{rects[a]} overlaps {rects[b]}.");
        Assert.Equal(width * height, rects.Sum(rect => rect.Width * rect.Height), 3);
        Assert.All(rects, rect => Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.Right <= width + 1e-6 && rect.Bottom <= height + 1e-6, $"{rect} leaves the area."));
    }

    [Fact]
    public void ASplitHalvesOnePaneAndMovesNoOther()
    {
        var one = Leaf("a");
        var two = PaneLayout.Split(one, "a", PaneSplit.Columns, "b");
        Assert.Equal(Cols(0.5, Leaf("a"), Leaf("b")), two);
        var three = PaneLayout.Split(two, "b", PaneSplit.Rows, "c");
        Assert.Equal(Cols(0.5, Leaf("a"), Rows(0.5, Leaf("b"), Leaf("c"))), three);
        Assert.Equal(["a", "b", "c"], PaneLayout.Leaves(three));
        // The new pane follows the one it split, in the order.
        var four = PaneLayout.Split(three, "a", PaneSplit.Rows, "d");
        Assert.Equal(["a", "d", "b", "c"], PaneLayout.Leaves(four));
        var before = PaneLayout.Arrange(three, 1000, 600, Divider);
        var after = PaneLayout.Arrange(four, 1000, 600, Divider);
        Assert.Equal(RectOf(before, "b"), RectOf(after, "b"));
        Assert.Equal(RectOf(before, "c"), RectOf(after, "c"));
        var a = RectOf(after, "a"); var d = RectOf(after, "d");
        Assert.Equal(RectOf(before, "a").Width, a.Width);
        Assert.Equal(a.Width, d.Width);
        Assert.Equal(RectOf(before, "a").Height, a.Height + Divider + d.Height, 6);
        Assert.True(d.Y > a.Y, "The new pane of a rows split opens below the pane it split.");
    }

    [Fact]
    public void SplittingRefusesUnknownPanesDuplicatesAndTheSeventeenth()
    {
        var tree = Cols(0.5, Leaf("a"), Leaf("b"));
        Assert.Throws<ArgumentException>(() => PaneLayout.Split(tree, "z", PaneSplit.Columns, "c"));
        Assert.Throws<ArgumentException>(() => PaneLayout.Split(tree, "a", PaneSplit.Columns, "b"));
        var full = Leaf("p0");
        for (var index = 1; index < PaneLayout.MaxPanes; index++) full = PaneLayout.Split(full, "p" + (index - 1), index % 2 == 0 ? PaneSplit.Columns : PaneSplit.Rows, "p" + index);
        Assert.Equal(PaneLayout.MaxPanes, PaneLayout.Leaves(full).Count);
        var refused = Assert.Throws<InvalidOperationException>(() => PaneLayout.Split(full, "p0", PaneSplit.Columns, "one more"));
        Assert.Contains("up to 16 panes", refused.Message);
    }

    [Fact]
    public void InsertingATreePutsItOnTheChosenSideOfOnePaneAndMovesNoOther()
    {
        var tree = Cols(0.6, Leaf("a"), Leaf("b"));
        var coming = Rows(0.3, Leaf("x"), Leaf("y"));
        // Below b: b's leaf becomes rows of b over the subtree, which keeps its own tiling and ratio.
        var below = PaneLayout.Insert(tree, "b", PaneDirection.Down, coming);
        Assert.Equal(Cols(0.6, Leaf("a"), Rows(0.5, Leaf("b"), coming)), below);
        Assert.Equal(["a", "b", "x", "y"], PaneLayout.Leaves(below));
        // Left of a: the subtree comes first, so it leads the order and a keeps the right half of its old room.
        var left = PaneLayout.Insert(tree, "a", PaneDirection.Left, coming);
        Assert.Equal(Cols(0.6, Cols(0.5, coming, Leaf("a")), Leaf("b")), left);
        Assert.Equal(["x", "y", "a", "b"], PaneLayout.Leaves(left));
        Assert.Equal(Cols(0.6, Rows(0.5, coming, Leaf("a")), Leaf("b")), PaneLayout.Insert(tree, "a", PaneDirection.Up, coming));
        Assert.Equal(Cols(0.6, Leaf("a"), Cols(0.5, Leaf("b"), coming)), PaneLayout.Insert(tree, "b", PaneDirection.Right, coming));
        var before = PaneLayout.Arrange(tree, 1000, 600, Divider);
        var after = PaneLayout.Arrange(below, 1000, 600, Divider);
        Assert.Equal(RectOf(before, "a"), RectOf(after, "a"));
        var b = RectOf(after, "b"); var x = RectOf(after, "x"); var y = RectOf(after, "y");
        Assert.Equal(RectOf(before, "b").Width, b.Width);
        Assert.Equal(RectOf(before, "b").Height, b.Height + Divider + x.Height + Divider + y.Height, 6);
        Assert.True(x.Y > b.Y && y.Y > x.Y, "The dropped panes lie below the pane dropped on, in their own order.");
        AssertTiled(after, 1000, 600);
    }

    [Fact]
    public void InsertingRefusesAnUnknownPaneADuplicateAndMoreThanSixteen()
    {
        var tree = Cols(0.5, Leaf("a"), Leaf("b"));
        Assert.Throws<ArgumentException>(() => PaneLayout.Insert(tree, "z", PaneDirection.Down, Leaf("c")));
        Assert.Throws<ArgumentException>(() => PaneLayout.Insert(tree, "a", PaneDirection.Down, Rows(0.5, Leaf("c"), Leaf("b"))));
        var fifteen = Leaf("p0");
        for (var index = 1; index < 15; index++) fifteen = PaneLayout.Split(fifteen, "p" + (index - 1), PaneSplit.Columns, "p" + index);
        Assert.Equal(16, PaneLayout.Leaves(PaneLayout.Insert(fifteen, "p0", PaneDirection.Right, Leaf("one more"))).Count);
        var refused = Assert.Throws<InvalidOperationException>(() => PaneLayout.Insert(fifteen, "p0", PaneDirection.Right, Cols(0.5, Leaf("q"), Leaf("r"))));
        Assert.Contains("up to 16 panes", refused.Message);
    }

    [Theory]
    [InlineData(500, 150, PaneDirection.Down)]
    [InlineData(500, 149, PaneDirection.Up)]
    [InlineData(500, 151, PaneDirection.Down)]
    [InlineData(30, 150, PaneDirection.Left)]
    [InlineData(970, 150, PaneDirection.Right)]
    [InlineData(500, 20, PaneDirection.Up)]
    [InlineData(500, 280, PaneDirection.Down)]
    // The sides meet on the rectangle's diagonals, not at 45°: a wide pane still has a left and a right wedge at its centre line.
    [InlineData(200, 150, PaneDirection.Left)]
    [InlineData(200, 60, PaneDirection.Up)]
    [InlineData(800, 240, PaneDirection.Down)]
    [InlineData(-10, -10, PaneDirection.Up)]
    public void TheDropSideIsTheNearestEdgeInSharesOfTheRectangle(double x, double y, PaneDirection expected) =>
        Assert.Equal(expected, PaneLayout.DropSide(new PaneRect(0, 0, 1000, 300), x, y));

    [Fact]
    public void TheDropAreaIsTheHalfInsertWillGiveTheDroppedTree()
    {
        var rect = new PaneRect(100, 50, 1000, 600);
        foreach (var side in new[] { PaneDirection.Left, PaneDirection.Right, PaneDirection.Up, PaneDirection.Down })
        {
            var tree = PaneLayout.Insert(Leaf("a"), "a", side, Leaf("x"));
            var placed = PaneLayout.Arrange(tree, rect.Width, rect.Height, Divider);
            var expected = RectOf(placed, "x");
            var area = PaneLayout.DropArea(rect, side, Divider);
            Assert.Equal(expected with { X = expected.X + rect.X, Y = expected.Y + rect.Y }, area);
        }
        Assert.Equal(new PaneRect(100, 50, 496, 600), PaneLayout.DropArea(rect, PaneDirection.Left, Divider));
        Assert.Equal(new PaneRect(100, 354, 1000, 296), PaneLayout.DropArea(rect, PaneDirection.Down, Divider));
        // A rectangle too small for a divider still answers, with nothing past its edge.
        var tiny = PaneLayout.DropArea(new PaneRect(0, 0, 4, 4), PaneDirection.Right, Divider);
        Assert.True(tiny.X >= 0 && tiny.Right <= 4 && tiny.Width >= 0);
    }

    [Fact]
    public void ClosingAPaneHandsItsRoomToTheOtherSideOfItsSplit()
    {
        var tree = Cols(0.4, Rows(0.6, Leaf("a"), Leaf("d")), Rows(0.5, Leaf("b"), Leaf("c")));
        var without = PaneLayout.Remove(tree, "b");
        Assert.Equal(Cols(0.4, Rows(0.6, Leaf("a"), Leaf("d")), Leaf("c")), without);
        Assert.Equal(["a", "d", "c"], PaneLayout.Leaves(without!));
        var before = PaneLayout.Arrange(tree, 1000, 600, Divider);
        var after = PaneLayout.Arrange(without!, 1000, 600, Divider);
        Assert.Equal(RectOf(before, "a"), RectOf(after, "a"));
        Assert.Equal(RectOf(before, "d"), RectOf(after, "d"));
        Assert.Equal(new PaneRect(RectOf(before, "b").X, 0, RectOf(before, "b").Width, 600), RectOf(after, "c"));
        Assert.Null(PaneLayout.Remove(Leaf("only"), "only"));
        Assert.Throws<ArgumentException>(() => PaneLayout.Remove(tree, "z"));
    }

    [Theory]
    [InlineData(1000, 600)]
    [InlineData(300, 200)]
    [InlineData(20, 10)]
    [InlineData(0, 0)]
    public void ArrangementTilesTheWholeAreaWithoutOverlapAtAnySize(double width, double height)
    {
        var tree = Cols(0.3, Rows(0.7, Leaf("a"), Cols(0.5, Leaf("d"), Leaf("e"))), Rows(0.5, Leaf("b"), Rows(0.85, Leaf("c"), Leaf("f"))));
        var arrangement = PaneLayout.Arrange(tree, width, height, Divider);
        Assert.Equal(6, arrangement.Panes.Count);
        Assert.Equal(5, arrangement.Dividers.Count);
        AssertTiled(arrangement, width, height);
        Assert.Equal(PaneLayout.Leaves(tree), arrangement.Panes.Select(placement => placement.Pane));
        Assert.Equal(PaneLayout.Splits(tree).Select(split => split.Path), arrangement.Dividers.Select(divider => divider.Path));
    }

    [Fact]
    public void RatiosAreHonouredAndMinimumsKeptWhileTheRoomHoldsThem()
    {
        var tree = Cols(0.3, Leaf("a"), Leaf("b"));
        var wide = PaneLayout.Arrange(tree, 1008, 600, Divider);
        Assert.Equal(new PaneRect(0, 0, 300, 600), RectOf(wide, "a"));
        Assert.Equal(new PaneRect(300, 0, Divider, 600), wide.Dividers[0].Rect);
        Assert.Equal(new PaneRect(308, 0, 700, 600), RectOf(wide, "b"));
        Assert.Equal((300, 1000), (wide.Dividers[0].Before, wide.Dividers[0].Room));
        // 0.3 of 400 is 120, under the 160 minimum: the first side is held at 160 while there is room for two.
        var narrow = PaneLayout.Arrange(tree, 408, 600, Divider);
        Assert.Equal(160, RectOf(narrow, "a").Width);
        Assert.Equal(240, RectOf(narrow, "b").Width);
        // Below two minimums the ratio is taken as it is.
        var tight = PaneLayout.Arrange(tree, 208, 600, Divider);
        Assert.Equal(60, RectOf(tight, "a").Width);
        Assert.Equal(140, RectOf(tight, "b").Width);
        var rows = PaneLayout.Arrange(Rows(0.85, Leaf("a"), Leaf("b")), 600, 308, Divider);
        Assert.Equal(180, RectOf(rows, "a").Height);
        Assert.Equal(120, RectOf(rows, "b").Height);
    }

    [Fact]
    public void ADividerDragSetsOneSplitsRatioWithinBounds()
    {
        var tree = Cols(0.5, Leaf("a"), Rows(0.5, Leaf("b"), Leaf("c")));
        var dragged = PaneLayout.WithRatio(tree, "1", 0.7);
        Assert.Equal(Cols(0.5, Leaf("a"), Rows(0.7, Leaf("b"), Leaf("c"))), dragged);
        Assert.Equal(0.9, PaneLayout.WithRatio(tree, "", 0.99).Ratio);
        Assert.Throws<ArgumentException>(() => PaneLayout.WithRatio(tree, "0", 0.5));
        Assert.Throws<ArgumentException>(() => PaneLayout.WithRatio(tree, "x", 0.5));
        // 1000 px of room: a drag may not leave less than 160 on either side, so 0.1 becomes 0.16 and 0.95 becomes 0.84.
        Assert.Equal(0.16, PaneLayout.ClampRatio(0.1, 1000, 160), 6);
        Assert.Equal(0.84, PaneLayout.ClampRatio(0.95, 1000, 160), 6);
        Assert.Equal(0.4, PaneLayout.ClampRatio(0.4, 1000, 160), 6);
        // With too little room for two minimums only the range applies.
        Assert.Equal(0.1, PaneLayout.ClampRatio(0.05, 200, 160), 6);
    }

    [Fact]
    public void EvenGivesEveryPaneTheSameShareWhateverTheTreesShape()
    {
        // A pane beside a column of two: a third for the pane, two thirds for the column, halved.
        var chain = Cols(0.5, Leaf("a"), Rows(0.5, Leaf("b"), Leaf("c")));
        var even = PaneLayout.Even(chain);
        Assert.Equal(Cols(1.0 / 3, Leaf("a"), Rows(0.5, Leaf("b"), Leaf("c"))), even);
        var thirds = PaneLayout.Arrange(even, 1, 1, divider: 0, minWidth: 0, minHeight: 0).Panes;
        Assert.All(thirds, placement => Assert.Equal(1.0 / 3, placement.Rect.Width * placement.Rect.Height, 9));
        // Six panes in an uneven tree: each a sixth of the area, the leaves in the same order.
        var tree = Cols(0.3, Rows(0.7, Leaf("a"), Cols(0.35, Leaf("d"), Leaf("e"))), Rows(0.45, Leaf("b"), Rows(0.85, Leaf("c"), Leaf("f"))));
        var sixths = PaneLayout.Arrange(PaneLayout.Even(tree), 1, 1, divider: 0, minWidth: 0, minHeight: 0).Panes;
        Assert.All(sixths, placement => Assert.Equal(1.0 / 6, placement.Rect.Width * placement.Rect.Height, 9));
        Assert.Equal(PaneLayout.Leaves(tree), PaneLayout.Leaves(PaneLayout.Even(tree)));
        // A leaf is its own even tree; a pane beside eleven is held at the least a divider allows.
        Assert.Equal(Leaf("a"), PaneLayout.Even(Leaf("a")));
        var eleven = Enumerable.Range(1, 11).Select(index => Leaf(index.ToString())).Aggregate((first, second) => Cols(0.5, first, second));
        Assert.Equal(PaneLayout.MinRatio, PaneLayout.Even(Cols(0.5, Leaf("z"), eleven)).Ratio);
    }

    [Fact]
    public void UntouchedMeansEverySplitIsStillAtTheHalf()
    {
        Assert.True(PaneLayout.Untouched(Leaf("a")));
        Assert.True(PaneLayout.Untouched(Cols(0.5, Leaf("a"), Rows(0.5, Leaf("b"), Leaf("c")))));
        // A missing ratio reads as the half.
        Assert.True(PaneLayout.Untouched(new PaneNode(Split: "columns", First: Leaf("a"), Second: Leaf("b"))));
        Assert.False(PaneLayout.Untouched(Cols(0.5, Leaf("a"), Rows(0.7, Leaf("b"), Leaf("c")))));
        Assert.False(PaneLayout.Untouched(PaneLayout.Even(Cols(0.5, Leaf("a"), Rows(0.5, Leaf("b"), Leaf("c"))))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ATabSavedBeforeTreesTilesAsItsGridDid(int count)
    {
        var panes = Enumerable.Range(0, count).Select(index => "p" + index).ToList();
        var tree = PaneLayout.Legacy(panes, 0.4, 0.65);
        var arrangement = PaneLayout.Arrange(tree, 1008, 608, Divider, minWidth: 0, minHeight: 0);
        AssertTiled(arrangement, 1008, 608);
        Assert.Equal(count, arrangement.Panes.Count);
        // Two: side by side at the column ratio. Three: the right-hand pane split at the row ratio. Four: a 2×2 grid, top-left,
        // top-right, bottom-right, bottom-left in the saved order.
        if (count >= 2) { Assert.Equal(400, RectOf(arrangement, "p0").Width); Assert.Equal(408, RectOf(arrangement, "p1").X); }
        if (count == 2) Assert.Equal(608, RectOf(arrangement, "p1").Height);
        if (count == 3) { Assert.Equal(608, RectOf(arrangement, "p0").Height); Assert.Equal(390, RectOf(arrangement, "p1").Height); Assert.Equal(398, RectOf(arrangement, "p2").Y); }
        if (count == 4)
        {
            Assert.Equal(new PaneRect(0, 0, 400, 390), RectOf(arrangement, "p0"));
            Assert.Equal(new PaneRect(408, 0, 600, 390), RectOf(arrangement, "p1"));
            Assert.Equal(new PaneRect(408, 398, 600, 210), RectOf(arrangement, "p2"));
            Assert.Equal(new PaneRect(0, 398, 400, 210), RectOf(arrangement, "p3"));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PaneLayout.Legacy(Enumerable.Range(0, 5).Select(index => "p" + index).ToList(), null, null));
    }

    [Fact]
    public void TreeOfReadsASavedTreeOrDerivesOneAndRefusesAMismatch()
    {
        SavedPane Pane(string id) => new(id, "shell", id, @"C:\work", "");
        var legacy = new SavedTab("t", "T", [Pane("a"), Pane("b"), Pane("c")], SplitRatio: 0.4, RowRatio: 0.3);
        Assert.Equal(Cols(0.4, Leaf("a"), Rows(0.3, Leaf("b"), Leaf("c"))), PaneLayout.TreeOf(legacy));
        var saved = new SavedTab("t", "T", [Pane("a"), Pane("b")], Layout: Rows(0.6, Leaf("b"), Leaf("a")));
        Assert.Same(saved.Layout, PaneLayout.TreeOf(saved));
        Assert.Contains("does not match", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(saved with { Panes = [Pane("a"), Pane("z")] })).Message);
        Assert.Contains("twice", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(saved with { Layout = Rows(0.5, Leaf("a"), Leaf("a")), Panes = [Pane("a"), Pane("a")] })).Message);
        Assert.Contains("direction", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(saved with { Layout = new PaneNode(Split: "diagonal", First: Leaf("a"), Second: Leaf("b")) })).Message);
        Assert.Contains("missing a side", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(saved with { Layout = new PaneNode(Split: "rows", First: Leaf("a")), Panes = [Pane("a")] })).Message);
        Assert.Contains("needs a layout", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(new SavedTab("t", "T", Enumerable.Range(0, 5).Select(index => Pane("p" + index)).ToList()))).Message);
        Assert.Contains("one to 16", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(new SavedTab("t", "T", []))).Message);
        Assert.Contains("one to 16", Assert.Throws<InvalidDataException>(() => PaneLayout.TreeOf(new SavedTab("t", "T", Enumerable.Range(0, 17).Select(index => Pane("p" + index)).ToList()))).Message);
        // The words are read whatever their case, so a hand-edited file still loads.
        Assert.Equal(PaneSplit.Columns, PaneLayout.DirectionOf(new PaneNode(Split: "Columns", First: Leaf("a"), Second: Leaf("b"))));
        Assert.Null(PaneLayout.DirectionOf(Leaf("a")));
    }

    [Fact]
    public void APaneMayBeSplitOnlyWhereTwoMinimumPanesAndADividerFit()
    {
        Assert.Equal(328, PaneLayout.LeastToSplit(PaneSplit.Columns, Divider));
        Assert.Equal(248, PaneLayout.LeastToSplit(PaneSplit.Rows, Divider));
        Assert.True(PaneLayout.CanSplit(PaneSplit.Columns, 328, 100, Divider));
        Assert.False(PaneLayout.CanSplit(PaneSplit.Columns, 327, 1000, Divider));
        Assert.True(PaneLayout.CanSplit(PaneSplit.Rows, 100, 248, Divider));
        Assert.False(PaneLayout.CanSplit(PaneSplit.Rows, 1000, 247, Divider));
        // A pane not yet laid out has no size to refuse by.
        Assert.True(PaneLayout.CanSplit(PaneSplit.Columns, 0, 0, Divider));
        Assert.Equal(PaneSplit.Columns, PaneLayout.AutoSplit(800, 600));
        Assert.Equal(PaneSplit.Rows, PaneLayout.AutoSplit(400, 600));
        Assert.Equal(PaneSplit.Columns, PaneLayout.AutoSplit(500, 500));
    }

    /// <summary>The smoke's six panes: a over the halves d and e on the left; b over c over f on the right.</summary>
    private static readonly PaneNode Six = Cols(0.5, Rows(0.5, Leaf("a"), Cols(0.5, Leaf("d"), Leaf("e"))), Rows(0.5, Leaf("b"), Rows(0.5, Leaf("c"), Leaf("f"))));

    [Theory]
    [InlineData("a", PaneDirection.Right, "b")]
    [InlineData("a", PaneDirection.Down, "d")]
    [InlineData("a", PaneDirection.Left, null)]
    [InlineData("a", PaneDirection.Up, null)]
    [InlineData("b", PaneDirection.Left, "a")]
    [InlineData("b", PaneDirection.Down, "c")]
    [InlineData("c", PaneDirection.Left, "e")]
    [InlineData("c", PaneDirection.Up, "b")]
    [InlineData("c", PaneDirection.Down, "f")]
    [InlineData("d", PaneDirection.Right, "e")]
    [InlineData("d", PaneDirection.Up, "a")]
    [InlineData("d", PaneDirection.Down, null)]
    [InlineData("e", PaneDirection.Left, "d")]
    [InlineData("e", PaneDirection.Right, "c")]
    [InlineData("e", PaneDirection.Up, "a")]
    [InlineData("f", PaneDirection.Left, "e")]
    [InlineData("f", PaneDirection.Up, "c")]
    [InlineData("f", PaneDirection.Right, null)]
    public void AltArrowFindsThePaneAcrossTheDividerOnThatSide(string from, PaneDirection direction, string? expected) =>
        Assert.Equal(expected, PaneLayout.Neighbor(Six, from, direction));

    [Fact]
    public void AmongPanesOnThatSideTheOneFocusedLastWinsThenTheOneSharingMostOfTheEdge()
    {
        // e's right edge meets c and f, a quarter each: the first in the tree's order until one of them has been focused.
        Assert.Equal("f", PaneLayout.Neighbor(Six, "e", PaneDirection.Right, id => id == "f" ? 5 : id == "c" ? 3 : 0));
        Assert.Equal("c", PaneLayout.Neighbor(Six, "e", PaneDirection.Right, id => id == "c" ? 7 : id == "f" ? 3 : 0));
        Assert.Equal("f", PaneLayout.Neighbor(Six, "e", PaneDirection.Right, id => id == "f" ? 1 : 0));
        // c meets a for 30% of the left edge and b for 70%: the larger share, until a has been focused.
        var uneven = Cols(0.5, Rows(0.3, Leaf("a"), Leaf("b")), Leaf("c"));
        Assert.Equal("b", PaneLayout.Neighbor(uneven, "c", PaneDirection.Left));
        Assert.Equal("a", PaneLayout.Neighbor(uneven, "c", PaneDirection.Left, id => id == "a" ? 1 : 0));
        // A pane focused but not across that divider plays no part.
        Assert.Equal("b", PaneLayout.Neighbor(uneven, "c", PaneDirection.Left, id => id == "c" ? 9 : 0));
        Assert.Throws<ArgumentException>(() => PaneLayout.Neighbor(Six, "z", PaneDirection.Left));
    }

    [Fact]
    public void NeighboursAreFoundAtUnevenRatiosAndAgreeWithTheSidesReported()
    {
        // Edges built from different sums of ratios: e's right edge (0.3) is 0.35 + 0.65 of the left column's width.
        var tree = Cols(0.3, Rows(0.7, Leaf("a"), Cols(0.35, Leaf("d"), Leaf("e"))), Rows(0.45, Leaf("b"), Rows(0.85, Leaf("c"), Leaf("f"))));
        Assert.Equal("c", PaneLayout.Neighbor(tree, "e", PaneDirection.Right));
        Assert.Equal("e", PaneLayout.Neighbor(tree, "f", PaneDirection.Left));
        Assert.Equal("a", PaneLayout.Neighbor(tree, "c", PaneDirection.Left));
        Assert.Equal("a", PaneLayout.Neighbor(tree, "b", PaneDirection.Left));
        foreach (var pane in PaneLayout.Leaves(tree))
        {
            var sides = PaneLayout.NeighborsOf(tree, pane);
            Assert.Equal(sides.Left, PaneLayout.Neighbor(tree, pane, PaneDirection.Left) is not null);
            Assert.Equal(sides.Right, PaneLayout.Neighbor(tree, pane, PaneDirection.Right) is not null);
            Assert.Equal(sides.Up, PaneLayout.Neighbor(tree, pane, PaneDirection.Up) is not null);
            Assert.Equal(sides.Down, PaneLayout.Neighbor(tree, pane, PaneDirection.Down) is not null);
        }
        Assert.Equal(new PaneNeighbors(Left: false, Right: true, Up: false, Down: true), PaneLayout.NeighborsOf(Six, "a"));
        Assert.Equal(new PaneNeighbors(Left: true, Right: false, Up: true, Down: false), PaneLayout.NeighborsOf(Six, "f"));
        Assert.Equal(default, PaneLayout.NeighborsOf(Leaf("only"), "only"));
    }

    [Theory]
    [InlineData(null, 0.5)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(double.PositiveInfinity, 0.5)]
    [InlineData(-3.0, 0.1)]
    [InlineData(0.95, 0.9)]
    [InlineData(0.4, 0.4)]
    public void ClampRatioKeepsSavedRatiosInsideTheDividerRange(double? saved, double expected) =>
        Assert.Equal(expected, PaneLayout.ClampRatio(saved));
}
