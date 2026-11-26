using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

[Collection("Theme catalog")]
public sealed class ThemeCatalogTests
{
    [Fact]
    public void EveryThemeHasUniqueIdentityAndValidColors()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(theme => theme.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var theme in ThemeCatalog.All)
        {
            foreach (var property in typeof(ThemeDefinition).GetProperties().Where(item => item.PropertyType == typeof(string)))
            {
                if (property.Name is "Id" or "Name" or "Family" or "DisplayFont") continue;
                var value = (string?)property.GetValue(theme);
                Assert.True(ThemeCatalog.IsHex(value), $"{theme.Id}.{property.Name} = '{value}' is not RRGGBB or RRGGBBAA.");
            }
            foreach (var property in typeof(TerminalPalette).GetProperties())
            {
                var value = (string?)property.GetValue(theme.Terminal);
                Assert.True(ThemeCatalog.IsHex(value, allowAlpha: false), $"{theme.Id}.Terminal.{property.Name} = '{value}' must be opaque RRGGBB.");
            }
            Assert.True(ThemeCatalog.IsHex(theme.AgentHue("claude"), allowAlpha: false));
            Assert.True(ThemeCatalog.IsHex(theme.AgentHue("opencode"), allowAlpha: false));
            Assert.NotEqual(theme.AgentHue("opencode"), theme.AgentHue("shell"));
            Assert.True(ThemeCatalog.IsHex(theme.AgentFill("codex")));
            Assert.NotEqual(theme.Terminal.Background, theme.Terminal.Foreground);
        }
    }

    [Theory]
    [InlineData("Dark", "sic-dark")]
    [InlineData("Light", "sic-light")]
    [InlineData("lagoon-light", "lagoon-light")]
    [InlineData(null, "sic-dark")]
    [InlineData("", "sic-dark")]
    [InlineData("fluent-light", "fluent-light")]
    [InlineData("does-not-exist", "sic-dark")]
    public void FindMapsLegacyAndUnknownNamesOntoTheCatalog(string? saved, string expected)
    {
        Assert.Equal(expected, ThemeCatalog.Find(saved).Id);
    }

    /// <summary>The bold themes, Nord and Dracula are dark only (docs/decisions/built-in-themes.md); every other family offers both modes.</summary>
    private static readonly string[] SingleMode = ["aurora", "outrun", "nord", "dracula"];

    [Fact]
    public void EachFamilyOffersALightAndADarkModeButTheDarkOnlyThemes()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var other = ThemeCatalog.Toggle(theme);
            Assert.Equal(theme.Family, other.Family);
            if (SingleMode.Contains(theme.Id)) Assert.Equal(theme, other);
            else Assert.NotEqual(theme.Light, other.Light);
        }
    }

    [Theory]
    [InlineData("garnet-dark", "garnet-light")]
    [InlineData("garnet-light", "garnet-dark")]
    [InlineData("aurora", "aurora")]
    [InlineData("outrun", "outrun")]
    [InlineData("catppuccin-latte", "catppuccin-mocha")]
    [InlineData("catppuccin-frappe", "catppuccin-latte")]
    [InlineData("tokyo-night-day", "tokyo-night")]
    [InlineData("rose-pine-dawn", "rose-pine")]
    [InlineData("solarized-dark", "solarized-light")]
    [InlineData("dracula", "dracula")]
    public void TheAddedThemesShipBuiltIn(string id, string toggled)
    {
        Assert.True(ThemeCatalog.IsBuiltIn(id));
        Assert.Equal(toggled, ThemeCatalog.Toggle(ThemeCatalog.Find(id)).Id);
    }

    [Fact]
    public void SicThemesAreTheDefaultAndCarryTheBrandAccent()
    {
        Assert.Equal("sic-dark", ThemeCatalog.Default.Id);
        var dark = ThemeCatalog.Find("sic-dark");
        var light = ThemeCatalog.Find("sic-light");
        Assert.Equal("009DC1", dark.Accent);
        Assert.Equal("C04F1A", light.Accent);
        Assert.Equal("Sora", dark.DisplayFont);
        Assert.False(dark.Mica);
        Assert.False(light.SystemAccent);
    }

    [Fact]
    public void LagoonKeepsTheEarlierTealAsAThemeOfItsOwn()
    {
        var dark = ThemeCatalog.Find("lagoon-dark");
        Assert.Equal("Lagoon Dark", dark.Name);
        Assert.Equal("lagoon-light", ThemeCatalog.Toggle(dark).Id);
        Assert.NotEqual(ThemeCatalog.Default.Accent, dark.Accent);
    }
}