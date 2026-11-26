using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PanelLayoutTests
{
    [Theory]
    [InlineData(320, 1392, 320)]
    [InlineData(240, 1392, 240)]
    [InlineData(560, 1392, 560)]
    public void AWidthInsideEveryBoundIsKept(int requested, double shared, int expected) => Assert.Equal(expected, PanelLayout.ClampWidth(requested, shared));

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    [InlineData(-50)]
    public void TooNarrowRisesToTheMinimum(int requested) => Assert.Equal(PanelLayout.MinWidth, PanelLayout.ClampWidth(requested, 1392));

    [Theory]
    [InlineData(561)]
    [InlineData(900)]
    [InlineData(int.MaxValue)]
    public void TooWideFallsToTheMaximum(int requested) => Assert.Equal(PanelLayout.MaxWidth, PanelLayout.ClampWidth(requested, 1392));

    [Fact]
    public void TheTerminalAreaKeepsItsFloor()
    {
        // 850 px shared: a 500 px panel would leave 350 px of terminal, so the panel stops at 450.
        Assert.Equal(450, PanelLayout.ClampWidth(500, 850));
        Assert.Equal(450, PanelLayout.ClampWidth(450, 850));
        Assert.Equal(300, PanelLayout.ClampWidth(300, 850));
        Assert.Equal(449, PanelLayout.ClampWidth(560, 849.9));
    }

    [Fact]
    public void AWindowTooNarrowForBothFloorsKeepsThePanelMinimum() => Assert.Equal(PanelLayout.MinWidth, PanelLayout.ClampWidth(400, 500));

    [Fact]
    public void AnUnknownWindowAppliesOnlyThePanelBounds()
    {
        Assert.Equal(PanelLayout.MaxWidth, PanelLayout.ClampWidth(800));
        Assert.Equal(PanelLayout.MinWidth, PanelLayout.ClampWidth(10, double.NaN));
        Assert.Equal(PanelLayout.DefaultWidth, PanelLayout.ClampWidth(PanelLayout.DefaultWidth));
    }

    [Fact]
    public void WithTheRailOnTheLeftTheStripKeepsTheWholeCaptionClear()
    {
        Assert.Equal((138.0, 0.0), PanelLayout.CaptionReserve(right: false, 138, 48, 320));
        Assert.Equal((138.0, 0.0), PanelLayout.CaptionReserve(right: false, 138, 48, 0));
    }

    [Fact]
    public void WithTheRailOnTheRightThePanelHeaderKeepsClearWhatTheRailDoesNotCover()
    {
        // At 100 % the three buttons are 138 DIPs wide; the rail takes 48 of them and the minimize and maximize buttons sit
        // over the last 90 of the panel header.
        Assert.Equal((0.0, 90.0), PanelLayout.CaptionReserve(right: true, 138, 48, 320));
        Assert.Equal((0.0, 90.0), PanelLayout.CaptionReserve(right: true, 138, 48, 90));
    }

    [Fact]
    public void WithTheRailOnTheRightTheStripTakesOverWhatAClosingPanelUncovers()
    {
        Assert.Equal((30.0, 90.0), PanelLayout.CaptionReserve(right: true, 138, 48, 60));
        Assert.Equal((90.0, 90.0), PanelLayout.CaptionReserve(right: true, 138, 48, 0));
    }

    [Fact]
    public void NoCaptionButtonsReserveNothing()
    {
        Assert.Equal((0.0, 0.0), PanelLayout.CaptionReserve(right: true, 0, 48, 0));
        Assert.Equal((0.0, 0.0), PanelLayout.CaptionReserve(right: true, 40, 48, 0));
        Assert.Equal((0.0, 0.0), PanelLayout.CaptionReserve(right: false, double.NaN, 48, 0));
        Assert.Equal((90.0, 90.0), PanelLayout.CaptionReserve(right: true, 138, 48, double.NaN));
    }
}
