using System.Text.Json;
using Sic.Core.Appearance;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PaneZoomTests
{
    [Theory]
    [InlineData(10, PaneZoom.MinPercent)]
    [InlineData(80, 80)]
    [InlineData(100, PaneZoom.MaxPercent)]
    public void TheShareIsClamped(int percent, int expected) => Assert.Equal(expected, PaneZoom.Clamp(percent));

    [Fact]
    public void ASettingsFileWithoutTheZoomTakesTheDefaults()
    {
        var preferences = JsonSerializer.Deserialize<AppPreferences>("{}")!;
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
    public void ATabSavesItsZoomedPaneOnlyWhileOneIsZoomed()
    {
        var tab = new SavedTab("t", "Tab", [new SavedPane("p", "shell", "PowerShell", @"C:\", "")]);
        Assert.DoesNotContain("Zoomed", JsonSerializer.Serialize(tab));
        var zoomed = JsonSerializer.Deserialize<SavedTab>(JsonSerializer.Serialize(tab with { Zoomed = "p" }))!;
        Assert.Equal("p", zoomed.Zoomed);
    }
}
