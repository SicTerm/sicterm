using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class WindowPlacementTests
{
    // A 1920×1080 display with a 48 px taskbar, and the first-run size the app falls back to.
    private static readonly PixelRect Primary = new(0, 0, 1920, 1032);
    private static readonly WindowPlacement Fallback = new(0, 0, 1440, 900, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWindowOnTheDisplayIsKeptExactlyWithItsMaximizedFlag(bool maximized)
    {
        var saved = new WindowPlacement(120, 90, 1280, 800, maximized);
        Assert.Equal(saved, WindowPlacement.Fit(saved, Primary, Fallback));
    }

    [Fact]
    public void AWindowOnASecondDisplayLeftOfThePrimaryIsKept()
    {
        var left = new PixelRect(-2560, 0, 2560, 1400);
        var saved = new WindowPlacement(-2400, 100, 1600, 1000, false);
        Assert.Equal(saved, WindowPlacement.Fit(saved, left, Fallback));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWindowWhoseDisplayIsGoneIsRecentredAtTheFallbackSizeAndKeepsItsMaximizedFlag(bool maximized)
    {
        // Saved on a display to the right that has since been detached; DisplayArea's Nearest fallback hands over the primary.
        var saved = new WindowPlacement(2200, 150, 1600, 1000, maximized);
        Assert.Equal(new WindowPlacement(240, 66, 1440, 900, maximized), WindowPlacement.Fit(saved, Primary, Fallback));
    }

    [Fact]
    public void AWindowShowingJustTheMinimumStaysAndOneShowingLessIsRecentred()
    {
        var justEnough = new WindowPlacement(1920 - 200, 1032 - 120, 1280, 800, false);
        Assert.Equal(justEnough, WindowPlacement.Fit(justEnough, Primary, Fallback));
        var tooLittle = justEnough with { X = 1920 - 199 };
        Assert.Equal(new WindowPlacement(240, 66, 1440, 900, false), WindowPlacement.Fit(tooLittle, Primary, Fallback));
    }

    [Fact]
    public void AWindowSmallerThanTheMinimumIsKeptWhenWhollyOnTheDisplay()
    {
        var small = new WindowPlacement(1800, 950, 110, 70, false);
        Assert.Equal(small, WindowPlacement.Fit(small, Primary, Fallback));
    }

    [Fact]
    public void AnOversizedWindowIsShrunkToTheWorkAreaAndPulledInside()
    {
        // Saved on a 4K display, restored on 1080p: both sizes shrink, and the origin moves so the whole window fits.
        var saved = new WindowPlacement(-40, 200, 3000, 2000, false);
        Assert.Equal(new WindowPlacement(0, 0, 1920, 1032, false), WindowPlacement.Fit(saved, Primary, Fallback));
        var wide = new WindowPlacement(300, 100, 2400, 700, true);
        Assert.Equal(new WindowPlacement(0, 100, 1920, 700, true), WindowPlacement.Fit(wide, Primary, Fallback));
    }

    [Fact]
    public void TheTopEdgeIsNeverLeftAboveTheWorkArea()
    {
        var saved = new WindowPlacement(300, -400, 1280, 800, false);
        Assert.Equal(new WindowPlacement(300, 0, 1280, 800, false), WindowPlacement.Fit(saved, Primary, Fallback));
        var belowTaskbarAtTop = new PixelRect(0, 48, 1920, 1032);
        Assert.Equal(48, WindowPlacement.Fit(new WindowPlacement(300, 10, 1280, 800, false), belowTaskbarAtTop, Fallback).Y);
    }

    [Theory]
    [InlineData(0, 800)]
    [InlineData(1280, 0)]
    [InlineData(-5, -5)]
    public void ANonPositiveSizeIsRejectedForTheFallback(int width, int height) =>
        Assert.Equal(new WindowPlacement(240, 66, 1440, 900, false), WindowPlacement.Fit(new WindowPlacement(120, 90, width, height, false), Primary, Fallback));

    [Fact]
    public void TheFallbackItselfIsShrunkOnADisplaySmallerThanIt()
    {
        var small = new PixelRect(0, 0, 1280, 720);
        Assert.Equal(new WindowPlacement(0, 0, 1280, 720, false), WindowPlacement.Fit(new WindowPlacement(5000, 5000, 800, 600, false), small, Fallback));
    }
}
