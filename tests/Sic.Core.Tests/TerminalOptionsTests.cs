using Sic.Core.Appearance;
using Sic.Core.State;
using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TerminalOptionsTests
{
    [Theory]
    [InlineData("bar", "bar")]
    [InlineData("Block", "block")]
    [InlineData(" underline ", "underline")]
    [InlineData("beam", "bar")]
    [InlineData("", "bar")]
    [InlineData(null, "bar")]
    public void CursorStyleIsOneTheRendererKnows(string? saved, string expected) => Assert.Equal(expected, TerminalOptions.NormalizeCursorStyle(saved));

    [Theory]
    [InlineData(10_000, 10_000)]
    [InlineData(1_000, 1_000)]
    [InlineData(50_000, 50_000)]
    [InlineData(999, 1_000)]
    [InlineData(0, 1_000)]
    [InlineData(-5, 1_000)]
    [InlineData(50_001, 50_000)]
    [InlineData(int.MaxValue, 50_000)]
    public void ScrollbackStaysWithinItsBounds(int saved, int expected) => Assert.Equal(expected, TerminalOptions.ClampScrollback(saved));

    [Fact]
    public void DefaultsAreTheRendererDefaultsTheAppShippedWithAndCopyOnSelectIsOff()
    {
        var preferences = new AppPreferences();
        Assert.Equal("bar", preferences.CursorStyle);
        Assert.True(preferences.CursorBlink);
        Assert.Equal(10_000, preferences.Scrollback);
        Assert.False(preferences.CopyOnSelect);
        Assert.Equal(TerminalOptions.DefaultCursorStyle, TerminalOptions.CursorStyles[0]);
    }

    [Fact]
    public void EveryCursorStyleHasItsOwnLabel() =>
        Assert.Equal(TerminalOptions.CursorStyles.Count, TerminalOptions.CursorStyles.Select(TerminalOptions.CursorLabel).Distinct().Count());

    [Fact]
    public void TheTerminalPaletteIsTheSameWhateverTheAppTheme()
    {
        // A dark canvas, the SIC dark scheme, with an accent of its own.
        Assert.Equal("011726", ThemeCatalog.Terminal.Background);
        Assert.True(ThemeCatalog.IsHex(ThemeCatalog.TerminalAccent, allowAlpha: false));
        Assert.Same(ThemeCatalog.Terminal, ThemeCatalog.Find("sic-dark").Terminal);
    }

    [Fact]
    public void SettingsListsSixDifferentFontsTheDefaultFirst()
    {
        Assert.Equal(new AppPreferences().FontFamily, TerminalOptions.FontFamilies[0]);
        var first = TerminalOptions.FontFamilies.Select(family => family.Split(',')[0].Trim()).ToList();
        Assert.Equal(6, first.Distinct().Count());
        // Each font is Windows' own or one the page carries; each falls back to Cascadia Mono for the symbols it lacks.
        Assert.All(first, name => Assert.True(name is "Cascadia Mono" or "Consolas" || TerminalOptions.BundledFonts.Contains(name), name));
        Assert.All(TerminalOptions.FontFamilies.Skip(1), family => Assert.Contains("Cascadia Mono", family.Split(',').Skip(1).Select(part => part.Trim())));
        Assert.Equal(TerminalOptions.BundledFonts.Order(), first.Where(TerminalOptions.BundledFonts.Contains).Order());
    }

    [Fact]
    public void EachBundledFontShipsRegularAndBoldWithItsLicenceAndAFaceForEach()
    {
        var root = RepositoryRoot();
        var fonts = Path.Combine(root, "src", "terminal", "fonts");
        var css = File.ReadAllText(Path.Combine(root, "src", "terminal", "terminal.css"));
        var build = File.ReadAllText(Path.Combine(root, "src", "terminal", "build.mjs"));
        foreach (var family in TerminalOptions.BundledFonts)
        {
            var file = family.Replace(" ", "");
            Assert.True(File.Exists(Path.Combine(fonts, file + "-OFL.txt")), file);
            Assert.Contains("SIL Open Font License, Version 1.1", File.ReadAllText(Path.Combine(fonts, file + "-OFL.txt")));
            Assert.Contains($"['{family}', '{file}']", build);
            foreach (var (weight, value) in new[] { ("Regular", 400), ("Bold", 700) })
            {
                var woff2 = File.ReadAllBytes(Path.Combine(fonts, $"{file}-{weight}.woff2"));
                Assert.Equal("wOF2"u8.ToArray(), woff2[..4]);
                Assert.Contains($"@font-face{{font-family:\"{family}\";src:url(fonts/{file}-{weight}.woff2) format(\"woff2\");font-weight:{value};", css);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperIntelligentChat.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
