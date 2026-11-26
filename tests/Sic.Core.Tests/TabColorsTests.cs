using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TabColorsTests
{
    [Fact]
    public void PresetsAreEightUniqueOpaqueColorPairs()
    {
        Assert.Equal(8, TabColors.Presets.Count);
        Assert.Equal(TabColors.Presets.Count, TabColors.Presets.Select(preset => preset.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var preset in TabColors.Presets)
        {
            Assert.True(ThemeCatalog.IsHex(preset.OnDark, allowAlpha: false), preset.Id + " dark-strip shade");
            Assert.True(ThemeCatalog.IsHex(preset.OnLight, allowAlpha: false), preset.Id + " light-strip shade");
            Assert.NotEqual(preset.OnDark, preset.OnLight);
            Assert.False(ThemeCatalog.IsHex(preset.Id), "A preset id must never be mistaken for a custom color: " + preset.Id);
        }
    }

    [Theory]
    [InlineData("blue", true, "60A5FA")]
    [InlineData("blue", false, "2563EB")]
    [InlineData("12ab34", true, "12AB34")]
    [InlineData("12AB34", false, "12AB34")]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("teal", true, null)]
    [InlineData("12AB3480", true, null)]
    [InlineData("#12AB34", true, null)]
    [InlineData("none", true, null)]
    [InlineData("none", false, null)]
    public void ResolvePicksTheStripShadeOrTheExactCustomColor(string? stored, bool darkStrip, string? expected) =>
        Assert.Equal(expected, TabColors.Resolve(stored, darkStrip));

    [Fact]
    public void NormalizeKeepsPresetIdsAndUppercasesCustomColors()
    {
        Assert.Equal("purple", TabColors.Normalize("purple"));
        Assert.Equal("ABCDEF", TabColors.Normalize("abcdef"));
        Assert.Null(TabColors.Normalize("Purple"));
        Assert.Null(TabColors.Normalize("not-a-color"));
        Assert.True(TabColors.IsValid("red") && TabColors.IsValid("0A0B0C"));
        Assert.False(TabColors.IsValid("0A0B0CFF") || TabColors.IsValid(null));
        // A tab's own "no color", which stands over its folder's color, is kept as it is.
        Assert.True(TabColors.IsValid(TabColors.None));
        Assert.Equal(TabColors.None, TabColors.Normalize(TabColors.None));
    }

    [Fact]
    public void RememberMovesTheColorToTheFrontWithoutDuplicatesAndCapsTheList()
    {
        var recent = TabColors.Remember(["111111", "222222", "bad", "333333"], "222222");
        Assert.Equal(["222222", "111111", "333333"], recent);
        recent = TabColors.Remember(recent, "aaaaaa");
        recent = TabColors.Remember(recent, "BBBBBB");
        Assert.Equal(["BBBBBB", "AAAAAA", "222222", "111111"], recent);
        Assert.Equal(TabColors.RecentLimit, recent.Count);
        Assert.Throws<ArgumentException>(() => TabColors.Remember(recent, "blue"));
    }
    [Fact]
    public void ThemeColorsAreTheStandardHuesUnderAThemePrefix_KeptAsIdsAndNeverTakenForCustomColors()
    {
        Assert.Equal(TabColors.Presets.Select(preset => "theme-" + preset.Id), TabColors.ThemeRoles.Select(role => role.Id));
        foreach (var role in TabColors.ThemeRoles)
        {
            Assert.True(TabColors.IsThemeRole(role.Id));
            Assert.True(TabColors.IsValid(role.Id));
            Assert.Equal(role.Id, TabColors.Normalize(role.Id));
            Assert.False(TabColors.IsCustom(role.Id));
            Assert.Null(TabColors.Preset(role.Id));
        }
        Assert.False(TabColors.IsThemeRole("theme-teal"));
        Assert.False(TabColors.IsThemeRole("blue"));
        Assert.Null(TabColors.Normalize("theme-teal"));
        Assert.True(TabColors.IsCustom("12ab34"));
        Assert.False(TabColors.IsCustom("blue"));
        Assert.False(TabColors.IsCustom(TabColors.None));
        Assert.False(TabColors.IsCustom(null));
    }

    [Fact]
    public void AThemeColorTakesEachThemesOwnShade_AndTheStandardOneWithoutATheme()
    {
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            var palette = theme.Terminal;
            Assert.Equal(palette.Red.ToUpperInvariant(), TabColors.Resolve("theme-red", darkStrip: true, theme));
            Assert.Equal(palette.Blue.ToUpperInvariant(), TabColors.Resolve("theme-blue", darkStrip: false, theme));
            Assert.Equal(palette.Magenta.ToUpperInvariant(), TabColors.ThemeShade("theme-purple", theme));
            foreach (var role in TabColors.ThemeRoles)
                Assert.True(ThemeCatalog.IsHex(TabColors.ThemeShade(role.Id, theme), allowAlpha: false), theme.Id + " " + role.Id);
            // A standard preset and a custom color do not follow the theme.
            Assert.Equal("60A5FA", TabColors.Resolve("blue", darkStrip: true, theme));
            Assert.Equal("12AB34", TabColors.Resolve("12ab34", darkStrip: true, theme));
            Assert.Null(TabColors.ThemeShade("blue", theme));
        }
        Assert.Equal("60A5FA", TabColors.Resolve("theme-blue", darkStrip: true));
        Assert.Equal("2563EB", TabColors.Resolve("theme-blue", darkStrip: false));
    }

    [Fact]
    public void OrangeAndPinkLieHalfwayBetweenTheThemesOwnHues()
    {
        var theme = ThemeCatalog.Default with { Terminal = ThemeCatalog.Default.Terminal with { Red = "FF0000", Yellow = "FFFF00", Magenta = "8000FF" } };
        Assert.Equal("FF8000", TabColors.ThemeShade("theme-orange", theme));
        Assert.Equal("C00080", TabColors.ThemeShade("theme-pink", theme));
    }
}
