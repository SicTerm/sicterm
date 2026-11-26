using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

/// <summary>Shares a collection with <see cref="ThemeCatalogTests"/>: <see cref="ThemeCatalog.Reload"/> changes the catalog every test reads.</summary>
[Collection("Theme catalog")]
public sealed class ThemeFilesTests
{
    private static TerminalPalette Fallback(bool light) => ThemeCatalog.TerminalFor(light);

    [Fact]
    public void BuiltInThemesAreTheEmbeddedFilesInTheirOrder()
    {
        Assert.Equal(["sic-dark", "sic-light", "lagoon-dark", "lagoon-light", "fluent-dark", "fluent-light", "navy-dark", "navy-light",
            "garnet-dark", "garnet-light", "aurora", "outrun",
            "catppuccin-mocha", "catppuccin-macchiato", "catppuccin-frappe", "catppuccin-latte", "nord", "dracula", "solarized-dark", "solarized-light",
            "gruvbox-dark", "gruvbox-light", "tokyo-night", "tokyo-night-storm", "tokyo-night-moon", "tokyo-night-day",
            "rose-pine", "rose-pine-moon", "rose-pine-dawn", "one-half-dark", "one-half-light"], ThemeCatalog.BuiltIn.Select(theme => theme.Id));
        Assert.All(ThemeCatalog.BuiltIn, theme => Assert.True(ThemeCatalog.IsBuiltIn(theme.Id)));
        Assert.False(ThemeCatalog.IsBuiltIn("ocean-dark"));
        Assert.Equal(ThemeCatalog.BuiltIn[0].Terminal, ThemeCatalog.Terminal);
        Assert.Equal("011726", ThemeCatalog.TerminalFor(false).Background);
        Assert.Equal("FFFFFF", ThemeCatalog.TerminalFor(true).Background);
    }

    [Fact]
    public void Write_ThenParse_RoundTripsEveryBuiltInTheme()
    {
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            var text = ThemeFiles.Write(theme);
            Assert.Contains("\"id\": \"" + theme.Id + "\"", text);
            Assert.Contains("\"brightPurple\": \"#" + theme.Terminal.BrightMagenta + "\"", text);
            var parsed = ThemeFiles.Parse(text, out var errors);
            Assert.Empty(errors);
            Assert.Equal(theme, parsed);
        }
    }

    [Fact]
    public void Parse_AMinimalDarkFileDerivesTheRest()
    {
        const string json = """{ "id": "ocean-dark", "name": "Ocean dark", "light": false, "chrome": { "base": "#101820", "text": "#e8f0f8", "accent": "#4cc2ff" } }""";
        var theme = ThemeFiles.Parse(json, out var errors, Fallback);
        Assert.Empty(errors);
        Assert.NotNull(theme);
        Assert.Equal("ocean-dark", theme!.Id);
        Assert.Equal("Ocean dark", theme.Family);
        Assert.Equal("101820", theme.Base); Assert.Equal("101820", theme.Rail); Assert.Equal("101820", theme.Panel);
        Assert.Equal("E8F0F8", theme.Text); Assert.Equal("E8F0F8C5", theme.TextSecondary); Assert.Equal("E8F0F887", theme.TextTertiary); Assert.Equal("E8F0F8B3", theme.RailText);
        Assert.Equal("4CC2FF", theme.Accent); Assert.Equal("000000", theme.AccentText); Assert.Equal("4CC2FF33", theme.AccentSoft);
        Assert.Equal("FFFFFF0F", theme.Layer); Assert.Equal("FFFFFF14", theme.Hover); Assert.Equal("6CCB5F", theme.Success); Assert.Equal("E8F0F887", theme.Neutral);
        Assert.False(theme.Mica); Assert.False(theme.SystemAccent); Assert.Equal(ThemeFiles.DefaultDisplayFont, theme.DisplayFont);
        Assert.Equal(ThemeCatalog.TerminalFor(false), theme.Terminal);
    }

    [Fact]
    public void Parse_AMinimalLightFileTakesLightDefaultsAndWhiteInkOnADarkAccent()
    {
        const string json = """{ "id": "paper", "name": "Paper", "family": "Paper", "light": true, "mica": true, "systemAccent": true, "displayFont": "Georgia", "chrome": { "base": "#FAFAFA", "text": "#202020", "accent": "#005FB8" } }""";
        var theme = ThemeFiles.Parse(json, out var errors, Fallback);
        Assert.Empty(errors);
        Assert.Equal("FFFFFF", theme!.Panel); Assert.Equal("FFFFFF", theme.Layer); Assert.Equal("00000009", theme.Hover); Assert.Equal("0000000F", theme.Stroke);
        Assert.Equal("FFFFFF", theme.AccentText); Assert.Equal("005FB826", theme.AccentSoft); Assert.Equal("0F7B0F", theme.Success); Assert.Equal("2020209E", theme.TextSecondary);
        Assert.True(theme.Mica); Assert.True(theme.SystemAccent); Assert.Equal("Georgia", theme.DisplayFont);
        Assert.Equal(ThemeCatalog.TerminalFor(true), theme.Terminal);
    }

    [Fact]
    public void Parse_TakesAWindowsTerminalSchemeAsItIsPasted()
    {
        const string json = """
            {
              // Campbell, from Windows Terminal's settings, with a trailing comma and this comment.
              "id": "campbell-dark", "name": "Campbell", "light": false,
              "chrome": { "base": "#0C0C0C", "text": "#CCCCCC", "accent": "#3A96DD" },
              "terminal": {
                "name": "Campbell", "background": "#0c0c0c", "foreground": "#cccccc", "cursorColor": "#ffffff", "selectionBackground": "#ffffff",
                "black": "#0C0C0C", "red": "#C50F1F", "green": "#13A10E", "yellow": "#C19C00", "blue": "#0037DA", "purple": "#881798", "cyan": "#3A96DD", "white": "#CCCCCC",
                "brightBlack": "#767676", "brightRed": "#E74856", "brightGreen": "#16C60C", "brightYellow": "#F9F1A5", "brightBlue": "#3B78FF", "brightPurple": "#B4009E", "brightCyan": "#61D6D6", "brightWhite": "#F2F2F2",
              },
            }
            """;
        var theme = ThemeFiles.Parse(json, out var errors);
        Assert.Empty(errors);
        Assert.Equal("0C0C0C", theme!.Terminal.Background); Assert.Equal("CCCCCC", theme.Terminal.Foreground); Assert.Equal("FFFFFF", theme.Terminal.Cursor);
        Assert.Equal("881798", theme.Terminal.Magenta); Assert.Equal("B4009E", theme.Terminal.BrightMagenta); Assert.Equal("F2F2F2", theme.Terminal.BrightWhite);
    }

    [Fact]
    public void Parse_TakesAWindowsTerminalSchemeWithoutCursorOrSelectionColors()
    {
        // Windows Terminal's own Solarized Dark, from its defaults.json, has no selectionBackground; without a cursorColor
        // either, the cursor takes the foreground and the selection brightBlack (Solarized's base02, its highlight tone).
        const string json = """
            {
              "id": "solarized-dark", "name": "Solarized Dark", "light": false,
              "chrome": { "base": "#002B36", "text": "#93A1A1", "accent": "#268BD2" },
              "terminal": {
                "name": "Solarized Dark", "foreground": "#839496", "background": "#002B36",
                "black": "#002B36", "red": "#DC322F", "green": "#859900", "yellow": "#B58900", "blue": "#268BD2", "purple": "#D33682", "cyan": "#2AA198", "white": "#EEE8D5",
                "brightBlack": "#073642", "brightRed": "#CB4B16", "brightGreen": "#586E75", "brightYellow": "#657B83", "brightBlue": "#839496", "brightPurple": "#6C71C4", "brightCyan": "#93A1A1", "brightWhite": "#FDF6E3"
              }
            }
            """;
        var theme = ThemeFiles.Parse(json, out var errors);
        Assert.Empty(errors);
        Assert.Equal("839496", theme!.Terminal.Cursor);
        Assert.Equal("073642", theme.Terminal.Selection);
        Assert.Equal("D33682", theme.Terminal.Magenta);
    }
    [Theory]
    [InlineData("{}", "id is missing", "name is missing", "light is missing", "chrome is missing")]
    [InlineData("""{ "id": "x", "name": "X", "light": false, "chrome": { "base": "#101010", "text": "#EEEEEE", "accent": "#12345" } }""", "chrome.accent '#12345'")]
    [InlineData("""{ "id": "x", "name": "X", "light": false, "chrome": { "base": "#101010", "text": "#EEEEEE", "accent": "#4CC2FF", "hover": "grey" } }""", "chrome.hover 'grey'")]
    [InlineData("""{ "id": "my theme", "name": "X", "light": false, "chrome": { "base": "#101010", "text": "#EEEEEE", "accent": "#4CC2FF" } }""", "id 'my theme'")]
    [InlineData("""{ "id": "x", "name": "X", "light": false, "chrome": { "base": "#101010", "text": "#EEEEEE", "accent": "#4CC2FF" }, "terminal": { "background": "#000000" } }""", "terminal.foreground is missing", "terminal.brightWhite is missing")]
    [InlineData("{ nope", "not valid JSON")]
    [InlineData("null", "holds no theme")]
    public void Parse_NamesEveryFault(string json, params string[] expected)
    {
        Assert.Null(ThemeFiles.Parse(json, out var errors, Fallback));
        foreach (var fault in expected) Assert.Contains(errors, error => error.Contains(fault, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RefusesAFileWithoutATerminalSchemeWhenThereIsNoFallback()
    {
        const string json = """{ "id": "x", "name": "X", "light": false, "chrome": { "base": "#101010", "text": "#EEEEEE", "accent": "#4CC2FF" } }""";
        Assert.Null(ThemeFiles.Parse(json, out var errors));
        Assert.Contains(errors, error => error.StartsWith("terminal is missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("4CC2FF", "000000")]
    [InlineData("FCE100", "000000")]
    [InlineData("005FB8", "FFFFFF")]
    [InlineData("01263E", "FFFFFF")]
    public void Ink_IsBlackOrWhiteByContrast(string color, string ink)
    {
        Assert.Equal(ink, ThemeFiles.Ink(color));
    }

    [Fact]
    public void ReadFolder_ListsGoodFilesInNameOrderAndReportsTheRest()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sic-themes-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            static string Minimal(string id) => $$"""{ "id": "{{id}}", "name": "{{id}}", "light": false, "chrome": { "base": "#101010", "text": "#EEEEEE", "accent": "#4CC2FF" } }""";
            File.WriteAllText(Path.Combine(folder, "b-second.json"), Minimal("second"));
            File.WriteAllText(Path.Combine(folder, "a-first.json"), Minimal("first"));
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ \"id\": \"broken\" ");
            File.WriteAllText(Path.Combine(folder, "clash.json"), Minimal("sic-dark"));
            File.WriteAllText(Path.Combine(folder, "dupe.json"), Minimal("first"));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a theme");
            // A ready-made file copied in before its theme shipped with SicTerm: the built-in theme exactly, so passed over.
            File.WriteAllText(Path.Combine(folder, "nord.json"), ThemeFiles.Write(ThemeCatalog.Find("nord")));
            var (themes, problems) = ThemeFiles.ReadFolder(folder, ThemeCatalog.BuiltIn, Fallback);
            Assert.Equal(["first", "second"], themes.Select(theme => theme.Id));
            Assert.Equal(["broken.json", "clash.json", "dupe.json"], problems.Select(problem => problem.File));
            Assert.Contains("not valid JSON", problems[0].Error);
            Assert.Contains("a built-in theme's", problems[1].Error);
            Assert.Contains("a-first.json's", problems[2].Error);
            var (none, noProblems) = ThemeFiles.ReadFolder(Path.Combine(folder, "missing"), ThemeCatalog.BuiltIn, Fallback);
            Assert.Empty(none); Assert.Empty(noProblems);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Reload_PutsTheUsersThemesAfterTheBuiltInOnesAndANullFolderTakesThemAway()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sic-themes-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "ocean.json"), """{ "id": "ocean-dark", "name": "Ocean dark", "light": false, "chrome": { "base": "#101820", "text": "#E8F0F8", "accent": "#4CC2FF" } }""");
            File.WriteAllText(Path.Combine(folder, "bad.json"), "{");
            var problems = ThemeCatalog.Reload(folder);
            Assert.Equal(folder, ThemeCatalog.Folder);
            Assert.Equal(ThemeCatalog.BuiltIn.Count + 1, ThemeCatalog.All.Count);
            Assert.Equal("ocean-dark", ThemeCatalog.All[^1].Id);
            Assert.Equal("Ocean dark", ThemeCatalog.Find("ocean-dark").Name);
            Assert.Equal(["bad.json"], problems.Select(problem => problem.File));
            Assert.Same(problems, ThemeCatalog.Problems);
            // A theme alone in its family toggles to itself.
            Assert.Equal("ocean-dark", ThemeCatalog.Toggle(ThemeCatalog.Find("ocean-dark")).Id);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            ThemeCatalog.Reload(null);
        }
        Assert.Equal(ThemeCatalog.BuiltIn.Count, ThemeCatalog.All.Count);
        Assert.Empty(ThemeCatalog.Problems);
        Assert.Equal("sic-dark", ThemeCatalog.Find("ocean-dark").Id);
    }
}
