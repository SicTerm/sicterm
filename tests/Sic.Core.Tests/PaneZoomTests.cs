using System.Text.Json;
using Sic.Core.Appearance;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PaneZoomTests
{
    [Theory]
    [InlineData("screen", "screen")]
    [InlineData("window", "window")]
    [InlineData(" WINDOW ", "window")]
    [InlineData(" AREA ", "area")]
    [InlineData("area", "area")]
    [InlineData(null, "screen")]
    [InlineData("", "screen")]
    [InlineData("monitor", "screen")]
    public void AnUnknownWordGivesTheDefault(string? frame, string expected) => Assert.Equal(expected, PaneZoom.Normalize(frame));

    [Fact]
    public void EveryChoiceHasALabelAndNormalizesToItself()
    {
        foreach (var choice in PaneZoom.Choices)
        {
            Assert.Equal(choice, PaneZoom.Normalize(choice));
            Assert.NotEmpty(PaneZoom.Label(choice));
        }
        Assert.Contains(PaneZoom.Default, PaneZoom.Choices);
    }

    [Theory]
    [InlineData(10, PaneZoom.MinPercent)]
    [InlineData(80, 80)]
    [InlineData(100, PaneZoom.MaxPercent)]
    public void TheShareIsClamped(int percent, int expected) => Assert.Equal(expected, PaneZoom.Clamp(percent));

    [Fact]
    public void ASettingsFileWithoutTheZoomTakesTheDefaults()
    {
        var preferences = JsonSerializer.Deserialize<AppPreferences>("{}")!;
        Assert.Equal(PaneZoom.Default, preferences.PaneZoom);
        Assert.Equal(PaneZoom.DefaultPercent, preferences.PaneZoomPercent);
    }

    [Fact]
    public void TheCardIsTheShareOfTheFrameCentredInIt()
    {
        var card = PaneZoom.Card(new PaneRect(100, 40, 1000, 600), 80);
        Assert.Equal(new PaneRect(200, 100, 800, 480), card);
    }

    [Fact]
    public void TheCardIsInWholePixelsAndTheShareClamped()
    {
        var card = PaneZoom.Card(new PaneRect(0, 0, 1001, 777), 200);
        Assert.Equal(Math.Round(1001 * 0.9), card.Width);
        Assert.Equal(Math.Round(777 * 0.9), card.Height);
        Assert.Equal(card.X, Math.Round(card.X));
        Assert.Equal(card.Y, Math.Round(card.Y));
        Assert.True(card.X > 0 && card.Right < 1001 && card.Y > 0 && card.Bottom < 777);
    }

    [Fact]
    public void AnEmptyFrameGivesAnEmptyCard() => Assert.Equal(0, PaneZoom.Card(new PaneRect(5, 5, 0, 300), 80).Width);

    [Fact]
    public void TheScreenCardIsTheShareOfTheWorkAreaCentredOnTheBody()
    {
        // A 1920×1040 work area at 80% is 1536×832, centred on a body that holds it.
        var card = PaneZoom.ScreenCard(new PaneRect(-60, -40, 1920, 1000), 1920, 1040, 80);
        Assert.Equal(new PaneRect(132, 44, 1536, 832), card);
    }

    [Fact]
    public void TheScreenCardIsNoBiggerThanTheBody()
    {
        var card = PaneZoom.ScreenCard(new PaneRect(0, 0, 1000, 700), 1920, 1040, 80);
        Assert.Equal(new PaneRect(0, 0, 1000, 700), card);
    }

    [Theory]
    // A maximized 1080p window's body holds 80%: 832 + 2×40 ≤ 1010.
    [InlineData(1920, 1010, 80, true)]
    // The smoke's 1440×900 window does not: 1536 > 1440.
    [InlineData(1440, 870, 80, false)]
    // 1440 holds 70% (1344) across, but not 728 + 80 down in 800.
    [InlineData(1440, 800, 70, false)]
    [InlineData(1440, 870, 70, true)]
    public void TheBodyHoldsTheScreenCardWithTheStripToSpare(double bodyWidth, double bodyHeight, int percent, bool fits) =>
        Assert.Equal(fits, PaneZoom.ScreenCardFits(bodyWidth, bodyHeight, 1920, 1040, percent, 40));

    [Fact]
    public void ATabSavesItsZoomedPaneOnlyWhileOneIsZoomed()
    {
        var tab = new SavedTab("t", "Tab", [new SavedPane("p", "shell", "PowerShell", @"C:\", "")]);
        Assert.DoesNotContain("Zoomed", JsonSerializer.Serialize(tab));
        var zoomed = JsonSerializer.Deserialize<SavedTab>(JsonSerializer.Serialize(tab with { Zoomed = "p" }))!;
        Assert.Equal("p", zoomed.Zoomed);
    }
}
