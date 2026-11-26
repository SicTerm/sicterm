using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sic.Core.Appearance;

/// <summary>
/// A theme as a file holds it (docs/decisions/theme-files.md): the identity, the chrome colors and a terminal color scheme in
/// Windows Terminal's own shape, so any published scheme pastes in. Every color is "#RRGGBB" or, for the chrome, "#RRGGBBAA".
/// Only <c>id</c>, <c>name</c>, <c>light</c> and the chrome's <c>base</c>, <c>text</c> and <c>accent</c> are required; the
/// rest <see cref="ThemeFiles.Resolve"/> derives from those, the way the built-in themes set them. The built-in themes are
/// files of this shape too, embedded in Sic.Core, so every launch reads the format.
/// </summary>
public sealed class ThemeFile
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    /// <summary>The family whose light and dark themes Toggle moves between; the name when left out.</summary>
    public string? Family { get; set; }
    public bool? Light { get; set; }
    /// <summary>Whether the window's base is Mica rather than the base color; false when left out.</summary>
    public bool? Mica { get; set; }
    /// <summary>Whether the Windows accent color replaces the accent at runtime; false when left out.</summary>
    public bool? SystemAccent { get; set; }
    /// <summary>The headings' font; Segoe UI Variable Display when left out.</summary>
    public string? DisplayFont { get; set; }
    public ThemeChrome? Chrome { get; set; }
    /// <summary>A Windows Terminal color scheme; the built-in palette of the theme's mode when left out.</summary>
    public TerminalScheme? Terminal { get; set; }
}

/// <summary>The chrome's colors, as <see cref="ThemeDefinition"/> names them; see there for what each paints.</summary>
public sealed class ThemeChrome
{
    public string? Base { get; set; }
    public string? Rail { get; set; }
    public string? RailText { get; set; }
    public string? Panel { get; set; }
    public string? Layer { get; set; }
    public string? Hover { get; set; }
    public string? Pressed { get; set; }
    public string? Stroke { get; set; }
    public string? StrokeStrong { get; set; }
    public string? Text { get; set; }
    public string? TextSecondary { get; set; }
    public string? TextTertiary { get; set; }
    public string? Accent { get; set; }
    public string? AccentText { get; set; }
    public string? AccentSoft { get; set; }
    public string? Success { get; set; }
    public string? Warning { get; set; }
    public string? Danger { get; set; }
    public string? Neutral { get; set; }
}

/// <summary>
/// A terminal color scheme with Windows Terminal's keys, "purple" for magenta included, so a scheme from its settings pastes
/// in unchanged; cursorColor and selectionBackground are optional, as they are there (the foreground and brightBlack when left out).
/// </summary>
public sealed class TerminalScheme
{
    public string? Name { get; set; }
    public string? Background { get; set; }
    public string? Foreground { get; set; }
    public string? CursorColor { get; set; }
    public string? SelectionBackground { get; set; }
    public string? Black { get; set; }
    public string? Red { get; set; }
    public string? Green { get; set; }
    public string? Yellow { get; set; }
    public string? Blue { get; set; }
    public string? Purple { get; set; }
    public string? Cyan { get; set; }
    public string? White { get; set; }
    public string? BrightBlack { get; set; }
    public string? BrightRed { get; set; }
    public string? BrightGreen { get; set; }
    public string? BrightYellow { get; set; }
    public string? BrightBlue { get; set; }
    public string? BrightPurple { get; set; }
    public string? BrightCyan { get; set; }
    public string? BrightWhite { get; set; }
}

/// <summary>A theme file that could not be used, and why: shown in Settings and logged, never applied.</summary>
public sealed record ThemeProblem(string File, string Error);

/// <summary>Reads and writes <see cref="ThemeFile"/>s and resolves them into <see cref="ThemeDefinition"/>s.</summary>
public static class ThemeFiles
{
    public const string Extension = ".json";
    public const string DefaultDisplayFont = "Segoe UI Variable Display";

    /// <summary>Lenient on the way in: any case for the keys, comments and trailing commas allowed, as a hand-written file has them.</summary>
    public static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// Parses one file's text: the theme, or null with every error found, so a file is fixed in one pass. A file without a
    /// terminal scheme takes <paramref name="terminalFallback"/>'s for its mode, or is refused when there is none.
    /// </summary>
    public static ThemeDefinition? Parse(string json, out IReadOnlyList<string> errors, Func<bool, TerminalPalette>? terminalFallback = null)
    {
        var found = new List<string>();
        ThemeFile? file = null;
        try { file = JsonSerializer.Deserialize<ThemeFile>(json, ReadOptions); }
        catch (JsonException exception) { found.Add("not valid JSON: " + exception.Message); }
        if (found.Count == 0 && file is null) found.Add("the file holds no theme");
        var theme = found.Count == 0 ? Resolve(file!, found, terminalFallback) : null;
        errors = found;
        return found.Count == 0 ? theme : null;
    }

    /// <summary>
    /// A file's theme, with what it leaves out derived from what it gives: the rail and panel from the base, the quieter inks
    /// from the text, the accent's ink by contrast, the status colors from Windows' own, and the terminal scheme from the
    /// fallback. Every fault is added to <paramref name="errors"/>, named by its key; null when there is any.
    /// </summary>
    public static ThemeDefinition? Resolve(ThemeFile file, List<string> errors, Func<bool, TerminalPalette>? terminalFallback = null)
    {
        var id = (file.Id ?? "").Trim();
        if (id.Length == 0) errors.Add("id is missing: a short word of letters, digits, '-' or '_', such as \"ocean-dark\"");
        else if (id.Length > 64 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) errors.Add("id '" + id + "' must be letters, digits, '-', '_' or '.', up to 64 of them");
        var name = (file.Name ?? "").Trim();
        if (name.Length is 0 or > 80) errors.Add("name is missing, or longer than 80 characters");
        if (file.Light is null) errors.Add("light is missing: true for a light theme, false for a dark one");
        var light = file.Light ?? false;
        var family = (file.Family ?? "").Trim();
        if (family.Length == 0) family = name;
        if (family.Length > 80) errors.Add("family is longer than 80 characters");
        var font = (file.DisplayFont ?? "").Trim();
        if (font.Length == 0) font = DefaultDisplayFont;
        if (font.Length > 64) errors.Add("displayFont is longer than 64 characters");
        var chrome = file.Chrome;
        if (chrome is null) { errors.Add("chrome is missing: at least base, text and accent"); chrome = new ThemeChrome(); }
        var @base = Color(chrome.Base, "chrome.base", errors, required: true);
        var text = Color(chrome.Text, "chrome.text", errors, required: true);
        var accent = Color(chrome.Accent, "chrome.accent", errors, required: true);
        // The derived defaults follow the Windows themes' numbers: alpha inks on the text for the quieter inks, white or black
        // at low alpha for the layers, Windows' own status colors.
        string Derive(string? given, string field, Func<string> fallback) => Color(given, field, errors, required: false) ?? fallback();
        var rail = Derive(chrome.Rail, "chrome.rail", () => @base ?? "000000");
        var railText = Derive(chrome.RailText, "chrome.railText", () => Alpha(text, light ? "9E" : "B3"));
        var panel = Derive(chrome.Panel, "chrome.panel", () => light ? "FFFFFF" : @base ?? "000000");
        var layer = Derive(chrome.Layer, "chrome.layer", () => light ? "FFFFFF" : "FFFFFF0F");
        var hover = Derive(chrome.Hover, "chrome.hover", () => light ? "00000009" : "FFFFFF14");
        var pressed = Derive(chrome.Pressed, "chrome.pressed", () => light ? "00000006" : "FFFFFF0A");
        var stroke = Derive(chrome.Stroke, "chrome.stroke", () => light ? "0000000F" : "FFFFFF14");
        var strokeStrong = Derive(chrome.StrokeStrong, "chrome.strokeStrong", () => light ? "0000001A" : "FFFFFF1F");
        var textSecondary = Derive(chrome.TextSecondary, "chrome.textSecondary", () => Alpha(text, light ? "9E" : "C5"));
        var textTertiary = Derive(chrome.TextTertiary, "chrome.textTertiary", () => Alpha(text, light ? "72" : "87"));
        var accentText = Derive(chrome.AccentText, "chrome.accentText", () => Ink(accent));
        var accentSoft = Derive(chrome.AccentSoft, "chrome.accentSoft", () => Alpha(accent, light ? "26" : "33"));
        var success = Derive(chrome.Success, "chrome.success", () => light ? "0F7B0F" : "6CCB5F");
        var warning = Derive(chrome.Warning, "chrome.warning", () => light ? "9D5D00" : "FCE100");
        var danger = Derive(chrome.Danger, "chrome.danger", () => light ? "C42B1C" : "FF99A4");
        var neutral = Derive(chrome.Neutral, "chrome.neutral", () => Alpha(text, light ? "72" : "87"));
        TerminalPalette? terminal = null;
        if (file.Terminal is { } scheme) terminal = Palette(scheme, errors);
        else if (terminalFallback is not null) terminal = terminalFallback(light);
        else errors.Add("terminal is missing: a Windows Terminal color scheme (background, foreground and the sixteen colors; cursorColor and selectionBackground optional)");
        if (errors.Count > 0) return null;
        return new ThemeDefinition(id, name, family, light, file.Mica ?? false, file.SystemAccent ?? false,
            @base!, rail, railText, panel, layer, hover, pressed, stroke, strokeStrong,
            text!, textSecondary, textTertiary, accent!, accentText, accentSoft, success, warning, danger, neutral, font, terminal!);
    }

    /// <summary>The theme as a file, every value written, so a copy is a complete starting point.</summary>
    public static string Write(ThemeDefinition theme)
    {
        var t = theme.Terminal;
        var file = new ThemeFile
        {
            Id = theme.Id, Name = theme.Name, Family = theme.Family, Light = theme.Light, Mica = theme.Mica, SystemAccent = theme.SystemAccent, DisplayFont = theme.DisplayFont,
            Chrome = new ThemeChrome
            {
                Base = "#" + theme.Base, Rail = "#" + theme.Rail, RailText = "#" + theme.RailText, Panel = "#" + theme.Panel, Layer = "#" + theme.Layer,
                Hover = "#" + theme.Hover, Pressed = "#" + theme.Pressed, Stroke = "#" + theme.Stroke, StrokeStrong = "#" + theme.StrokeStrong,
                Text = "#" + theme.Text, TextSecondary = "#" + theme.TextSecondary, TextTertiary = "#" + theme.TextTertiary,
                Accent = "#" + theme.Accent, AccentText = "#" + theme.AccentText, AccentSoft = "#" + theme.AccentSoft,
                Success = "#" + theme.Success, Warning = "#" + theme.Warning, Danger = "#" + theme.Danger, Neutral = "#" + theme.Neutral
            },
            Terminal = new TerminalScheme
            {
                Name = theme.Name, Background = "#" + t.Background, Foreground = "#" + t.Foreground, CursorColor = "#" + t.Cursor, SelectionBackground = "#" + t.Selection,
                Black = "#" + t.Black, Red = "#" + t.Red, Green = "#" + t.Green, Yellow = "#" + t.Yellow, Blue = "#" + t.Blue, Purple = "#" + t.Magenta, Cyan = "#" + t.Cyan, White = "#" + t.White,
                BrightBlack = "#" + t.BrightBlack, BrightRed = "#" + t.BrightRed, BrightGreen = "#" + t.BrightGreen, BrightYellow = "#" + t.BrightYellow,
                BrightBlue = "#" + t.BrightBlue, BrightPurple = "#" + t.BrightMagenta, BrightCyan = "#" + t.BrightCyan, BrightWhite = "#" + t.BrightWhite
            }
        };
        return JsonSerializer.Serialize(file, WriteOptions) + Environment.NewLine;
    }

    /// <summary>
    /// Every theme in a folder's *.json files, in file-name order, and the files that could not be used with why: unreadable,
    /// invalid, an id that is a built-in theme's (<paramref name="builtIn"/>) or one another file already took. A file that is a
    /// built-in theme exactly, as a ready-made file copied in before its theme shipped with SicTerm is, is passed over and is no
    /// problem. A folder that does not exist holds no themes and is no problem.
    /// </summary>
    public static (IReadOnlyList<ThemeDefinition> Themes, IReadOnlyList<ThemeProblem> Problems) ReadFolder(string folder, IEnumerable<ThemeDefinition> builtIn, Func<bool, TerminalPalette> terminalFallback)
    {
        var themes = new List<ThemeDefinition>();
        var problems = new List<ThemeProblem>();
        if (!Directory.Exists(folder)) return (themes, problems);
        var shipped = builtIn.ToDictionary(theme => theme.Id, StringComparer.Ordinal);
        var taken = new Dictionary<string, string>(shipped.Keys.Select(id => new KeyValuePair<string, string>(id, "a built-in theme")), StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(folder, "*" + Extension).Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.GetFileName(path);
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { problems.Add(new(file, "could not be read: " + exception.Message)); continue; }
            var theme = Parse(json, out var errors, terminalFallback);
            if (theme is null) { problems.Add(new(file, string.Join("; ", errors))); continue; }
            if (shipped.TryGetValue(theme.Id, out var same) && same == theme) continue;
            if (taken.TryGetValue(theme.Id, out var holder)) { problems.Add(new(file, "id '" + theme.Id + "' is " + holder + "'s; choose another")); continue; }
            taken[theme.Id] = file;
            themes.Add(theme);
        }
        return (themes, problems);
    }

    /// <summary>A color as the catalog keeps it: without the '#', upper-case, RRGGBB or RRGGBBAA; null for none, with an error when it was required or malformed.</summary>
    private static string? Color(string? value, string field, List<string> errors, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) errors.Add(field + " is missing: \"#RRGGBB\"");
            return null;
        }
        var hex = value.Trim().TrimStart('#').ToUpperInvariant();
        if (ThemeCatalog.IsHex(hex)) return hex;
        errors.Add(field + " '" + value.Trim() + "' is not \"#RRGGBB\" or \"#RRGGBBAA\"");
        return null;
    }

    private static TerminalPalette? Palette(TerminalScheme scheme, List<string> errors)
    {
        var before = errors.Count;
        string Opaque(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value)) { errors.Add("terminal." + key + " is missing: \"#RRGGBB\""); return ""; }
            var hex = value.Trim().TrimStart('#').ToUpperInvariant();
            if (ThemeCatalog.IsHex(hex, allowAlpha: false)) return hex;
            errors.Add("terminal." + key + " '" + value.Trim() + "' is not \"#RRGGBB\"");
            return "";
        }
        // Windows Terminal leaves both optional (its own Solarized Dark has no selectionBackground): the cursor takes the
        // foreground, and the selection brightBlack, a dim tone the text stays readable on, as Solarized's base02 is.
        var foreground = Opaque(scheme.Foreground, "foreground");
        var brightBlack = Opaque(scheme.BrightBlack, "brightBlack");
        string Optional(string? value, string key, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : Opaque(value, key);
        var palette = new TerminalPalette(
            Opaque(scheme.Background, "background"), foreground, Optional(scheme.CursorColor, "cursorColor", foreground), Optional(scheme.SelectionBackground, "selectionBackground", brightBlack),
            Opaque(scheme.Black, "black"), Opaque(scheme.Red, "red"), Opaque(scheme.Green, "green"), Opaque(scheme.Yellow, "yellow"),
            Opaque(scheme.Blue, "blue"), Opaque(scheme.Purple, "purple"), Opaque(scheme.Cyan, "cyan"), Opaque(scheme.White, "white"),
            brightBlack, Opaque(scheme.BrightRed, "brightRed"), Opaque(scheme.BrightGreen, "brightGreen"), Opaque(scheme.BrightYellow, "brightYellow"),
            Opaque(scheme.BrightBlue, "brightBlue"), Opaque(scheme.BrightPurple, "brightPurple"), Opaque(scheme.BrightCyan, "brightCyan"), Opaque(scheme.BrightWhite, "brightWhite"));
        return errors.Count == before ? palette : null;
    }

    /// <summary>The color's RGB with the alpha given, whatever alpha it had.</summary>
    private static string Alpha(string? color, string alpha) => (color ?? "FFFFFF")[..6] + alpha;

    /// <summary>Black or white, whichever reads better on the color: the ink for text on the accent.</summary>
    public static string Ink(string? color)
    {
        var luminance = Luminance(color ?? "000000");
        var onBlack = (luminance + 0.05) / 0.05;
        var onWhite = 1.05 / (luminance + 0.05);
        return onBlack >= onWhite ? "000000" : "FFFFFF";
    }

    /// <summary>The WCAG relative luminance of an RRGGBB color, alpha ignored.</summary>
    public static double Luminance(string color)
    {
        double Channel(int offset)
        {
            var value = int.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }
}
