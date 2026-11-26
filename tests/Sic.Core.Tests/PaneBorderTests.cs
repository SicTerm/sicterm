using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PaneBorderTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(8, 8)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(9, 8)]
    [InlineData(100, 8)]
    public void AWidthIsKeptFromOneToEightPixels(int width, int expected) => Assert.Equal(expected, PaneBorder.Clamp(width));

    [Fact]
    public void TheDefaultIsTwoPixelsAndTheWidestStaysInsideTheTerminalsMargin()
    {
        Assert.Equal(2, PaneBorder.Default);
        Assert.Equal(PaneBorder.Default, new Sic.Core.State.AppPreferences().PaneBorderWidth);
        // The page leaves 8 px above and below the cells (terminal.css), so no border up to that covers any of them.
        Assert.Equal(1, PaneBorder.Min);
        Assert.Equal(8, PaneBorder.Max);
    }

    [Theory]
    [InlineData("thick", 3)]
    [InlineData(" THICK ", 3)]
    [InlineData("medium", 2)]
    [InlineData("thin", 2)]
    [InlineData(null, 2)]
    [InlineData("bold", 2)]
    public void AWordFrom0926KeepsThickAndReadsTheRestAsTheDefault(string? word, int expected) => Assert.Equal(expected, PaneBorder.FromWord(word));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("none", null, null)]
    [InlineData("not a color", null, null)]
    [InlineData("pink", "pink", "F472B6")]
    [InlineData("1a2b3c", "1A2B3C", "1A2B3C")]
    [InlineData("theme-pink", "theme-pink", "F472B6")]
    public void AColorIsAPresetInItsDarkShadeOrAnRRGGBBAndAnythingElseIsTheAccent(string? value, string? kept, string? painted)
    {
        Assert.Equal(kept, PaneBorder.NormalizeColor(value));
        Assert.Equal(painted, PaneBorder.ResolveColor(value));
        Assert.Null(new Sic.Core.State.AppPreferences().PaneBorderColor);
    }

    [Fact]
    public void AThemeColorIsPaintedInTheThemesShade()
    {
        var theme = ThemeCatalog.Default;
        Assert.Equal(theme.Terminal.Blue.ToUpperInvariant(), PaneBorder.ResolveColor("theme-blue", theme));
        Assert.Equal("F472B6", PaneBorder.ResolveColor("pink", theme));
    }

    [Fact]
    public void TheLabelGivesTheClampedWidthInPixels()
    {
        Assert.Equal("1 px", PaneBorder.Label(1));
        Assert.Equal("5 px", PaneBorder.Label(5));
        Assert.Equal("8 px", PaneBorder.Label(12));
    }
}
