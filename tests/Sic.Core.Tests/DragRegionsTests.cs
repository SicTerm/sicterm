using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class DragRegionsTests
{
    // The 48 px rail of a 700 px tall window at 100 %.
    private static readonly PixelRect Rail = new(0, 0, 48, 700);

    [Fact]
    public void WithoutHolesTheWholeAreaDrags() => Assert.Equal([Rail], DragRegions.Subtract(Rail, []));

    [Fact]
    public void AHoleOutsideTheAreaChangesNothing() => Assert.Equal([Rail], DragRegions.Subtract(Rail, [new(60, 10, 40, 40)]));

    [Fact]
    public void AHoleCoveringTheAreaLeavesNothing() => Assert.Empty(DragRegions.Subtract(Rail, [new(-5, -5, 100, 800)]));

    [Fact]
    public void AnEmptyAreaLeavesNothing() => Assert.Empty(DragRegions.Subtract(default, []));

    [Fact]
    public void TwoRailButtonsLeaveBandsAboveBetweenAndBelowAndAStripEitherSide()
    {
        // 40 px buttons inset 4 px, with the rail's 2 px spacing between them.
        var holes = new PixelRect[] { new(4, 38, 40, 40), new(4, 80, 40, 40) };
        PixelRect[] expected =
        [
            new(0, 0, 48, 38),
            new(0, 38, 4, 40), new(44, 38, 4, 40),
            new(0, 78, 48, 2),
            new(0, 80, 4, 40), new(44, 80, 4, 40),
            new(0, 120, 48, 580)
        ];
        Assert.Equal(expected, DragRegions.Subtract(Rail, holes));
    }

    [Fact]
    public void ButtonsThatTouchLeaveOneStripDownEachSide()
    {
        var holes = new PixelRect[] { new(4, 38, 40, 40), new(4, 78, 40, 40) };
        Assert.Equal([new(0, 0, 48, 38), new(0, 38, 4, 80), new(44, 38, 4, 80), new(0, 118, 48, 582)], DragRegions.Subtract(Rail, holes));
    }

    [Fact]
    public void AHeaderKeepsEverythingButItsButtonAndTheResizeGrip()
    {
        // A 320 px panel header after the rail, its 32 px Hide button 6 px from the right, and the 8 px resize grip that
        // straddles the panel's edge and reaches past the header on both sides.
        var header = new PixelRect(48, 0, 320, 36);
        var hide = new PixelRect(330, 2, 32, 32);
        var grip = new PixelRect(364, 0, 8, 900);
        Assert.Equal([new(48, 0, 316, 2), new(48, 2, 282, 32), new(362, 2, 2, 32), new(48, 34, 316, 2)], DragRegions.Subtract(header, [hide, grip]));
    }

    [Fact]
    public void EveryPixelIsCoveredOnceUnlessAHoleCoversIt()
    {
        var area = new PixelRect(3, 2, 20, 16);
        PixelRect[][] holeSets =
        [
            [new(5, 5, 4, 4)],
            [new(5, 5, 8, 4), new(9, 7, 8, 6)],
            [new(0, 0, 10, 30), new(15, 10, 20, 3)],
            [new(4, 3, 2, 2), new(8, 3, 2, 2), new(12, 3, 2, 2), new(4, 6, 10, 1), new(20, 15, 5, 5)],
            [new(3, 2, 20, 1), new(3, 17, 20, 1), new(3, 2, 1, 16), new(22, 2, 1, 16)]
        ];
        foreach (var holes in holeSets)
        {
            var pieces = DragRegions.Subtract(area, holes);
            for (var y = area.Y - 1; y <= area.Bottom; y++)
                for (var x = area.X - 1; x <= area.Right; x++)
                {
                    var inArea = x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom;
                    var inHole = holes.Any(hole => Contains(hole, x, y));
                    Assert.Equal(inArea && !inHole ? 1 : 0, pieces.Count(piece => Contains(piece, x, y)));
                }
            Assert.All(pieces, piece => Assert.True(piece.Width > 0 && piece.Height > 0));
        }
    }

    private static bool Contains(PixelRect rect, int x, int y) => x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;

    [Fact]
    public void DipsScaleByEdgesSoNeighboursStillTouch()
    {
        var left = DragRegions.FromDips(0, 0, 10.3, 36, 1.5);
        var right = DragRegions.FromDips(10.3, 0, 5, 36, 1.5);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(new PixelRect(0, 0, 15, 54), left);
        Assert.Equal(new PixelRect(15, 0, 8, 54), right);
    }

    [Theory]
    [InlineData(-4, 36, 1)]
    [InlineData(0, 36, 1)]
    [InlineData(20, double.NaN, 1)]
    [InlineData(20, 36, 0)]
    public void ASizeThatIsNotPositiveIsEmpty(double width, double height, double scale) =>
        Assert.Equal(default, DragRegions.FromDips(10, 10, width, height, scale));

    [Fact]
    public void IntersectKeepsTheOverlapOrNothing()
    {
        Assert.Equal(new PixelRect(40, 0, 8, 36), DragRegions.Intersect(new(0, 0, 48, 36), new(40, -10, 100, 100)));
        Assert.Equal(default, DragRegions.Intersect(new(0, 0, 48, 36), new(48, 0, 10, 10)));
    }
}
