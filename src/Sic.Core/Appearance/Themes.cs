namespace Sic.Core.Appearance;

/// <summary>Terminal canvas and the sixteen ANSI colors as RRGGBB strings for the xterm.js renderer.</summary>
public sealed record TerminalPalette(
    string Background, string Foreground, string Cursor, string Selection,
    string Black, string Red, string Green, string Yellow, string Blue, string Magenta, string Cyan, string White,
    string BrightBlack, string BrightRed, string BrightGreen, string BrightYellow, string BrightBlue, string BrightMagenta, string BrightCyan, string BrightWhite);

/// <summary>
/// One selectable appearance. Chrome values are RRGGBB or RRGGBBAA so layered themes can tint a Mica base;
/// the terminal palette stays opaque RRGGBB. <see cref="SystemAccent"/> themes replace <see cref="Accent"/> with the
/// Windows accent color at runtime. <see cref="Terminal"/> no longer colors a live terminal (every terminal uses
/// <see cref="ThemeCatalog.Terminal"/>); its background is the pane surface a placeholder page and the pane headers sit on,
/// and its full palette is kept as a candidate scheme for per-agent terminal colors (roadmap).
/// </summary>
public sealed record ThemeDefinition(
    string Id, string Name, string Family, bool Light, bool Mica, bool SystemAccent,
    string Base, string Rail, string RailText, string Panel, string Layer, string Hover, string Pressed,
    string Stroke, string StrokeStrong,
    string Text, string TextSecondary, string TextTertiary,
    string Accent, string AccentText, string AccentSoft,
    string Success, string Warning, string Danger, string Neutral,
    string DisplayFont,
    TerminalPalette Terminal)
{
    /// <summary>Identity hue for an agent; restrained per the visual direction and only used in tiles and accents.</summary>
    public string AgentHue(string agentId) => agentId switch
    {
        "hermes" => Light ? "9A6700" : "E3B341",
        "claude" => Light ? "B5502A" : "F0A47E",
        "codex" => Light ? "0F7A6B" : "5FD1B2",
        "opencode" => Light ? "6E4BB8" : "B9A3F0",
        _ => Light ? "2B579A" : "8FB6E8"
    };

    /// <summary>Tile fill behind an agent glyph: the hue at low alpha.</summary>
    public string AgentFill(string agentId) => AgentHue(agentId) + (Light ? "1F" : "26");
}

/// <summary>
/// Every theme SIC offers: the built-in ones, which are theme files embedded in this assembly (Appearance/Themes, in their
/// numbered order, the first the default), and the user's own from the themes folder under SIC's data folder, read by
/// <see cref="Reload"/> at launch and on request (docs/decisions/theme-files.md). A user file that cannot be used is listed
/// in <see cref="Problems"/> with its error and is not applied; one whose id is a built-in theme's is refused too, so the
/// built-ins always look as they ship, unless it is that theme exactly, which is passed over.
/// </summary>
public static class ThemeCatalog
{
    private const string ResourcePrefix = "Sic.Core.Themes.";
    private static readonly IReadOnlyList<ThemeDefinition> BuiltInThemes = LoadBuiltIn();
    private static IReadOnlyList<ThemeDefinition> _all = BuiltInThemes;
    private static IReadOnlyList<ThemeProblem> _problems = [];

    /// <summary>The themes that ship, in the order Settings lists them first. The first entry is the default.</summary>
    public static IReadOnlyList<ThemeDefinition> BuiltIn => BuiltInThemes;

    /// <summary>Every selectable theme: the built-in ones, then the user's files in file-name order, as of the last <see cref="Reload"/>.</summary>
    public static IReadOnlyList<ThemeDefinition> All => _all;

    /// <summary>The user's theme files that could not be used at the last <see cref="Reload"/>, each with its error.</summary>
    public static IReadOnlyList<ThemeProblem> Problems => _problems;

    /// <summary>The user's themes folder last read; null until <see cref="Reload"/> has run.</summary>
    public static string? Folder { get; private set; }

    public static ThemeDefinition Default => BuiltInThemes[0];

    /// <summary>
    /// The one palette every live terminal uses, whatever the app theme: SIC dark's. Hermes, Claude Code and Codex each choose
    /// their own colors for a dark or a light background; recoloring the canvas under a running agent when the app switches
    /// between light and dark left its interface half unreadable, so the app theme stops at the terminal's edge.
    /// </summary>
    public static TerminalPalette Terminal => BuiltInThemes[0].Terminal;

    /// <summary>The terminal's own accent (the drop outline, the current find match), chosen against <see cref="Terminal"/>.</summary>
    public const string TerminalAccent = "009DC1";

    /// <summary>
    /// Reads the user's theme files from <paramref name="folder"/> and puts them after the built-in themes; a null folder, or
    /// one that does not exist, leaves the built-in themes alone. Returns the files that could not be used.
    /// </summary>
    public static IReadOnlyList<ThemeProblem> Reload(string? folder)
    {
        Folder = folder;
        if (folder is null) { _all = BuiltInThemes; _problems = []; return _problems; }
        var (themes, problems) = ThemeFiles.ReadFolder(folder, BuiltInThemes, TerminalFor);
        _all = [.. BuiltInThemes, .. themes];
        _problems = problems;
        return problems;
    }

    public static bool IsBuiltIn(string? id) => BuiltInThemes.Any(theme => theme.Id == id);

    /// <summary>The built-in SIC palette for a mode: what a user theme without a terminal scheme takes.</summary>
    public static TerminalPalette TerminalFor(bool light) => (BuiltInThemes.FirstOrDefault(theme => theme.Family == "SIC" && theme.Light == light) ?? Default).Terminal;

    /// <summary>Maps a saved theme identifier, including earlier names, to a catalog entry. Unknown values fall back to the default.</summary>
    public static ThemeDefinition Find(string? id) => All.FirstOrDefault(theme => theme.Id == Normalize(id)) ?? Default;

    public static string Normalize(string? id) => id switch
    {
        "Dark" => "sic-dark",
        "Light" => "sic-light",
        null or "" => Default.Id,
        _ => id
    };

    /// <summary>The counterpart of a theme in the other mode of the same family, or the theme itself when the family has none.</summary>
    public static ThemeDefinition Toggle(ThemeDefinition theme) =>
        All.FirstOrDefault(candidate => candidate.Family == theme.Family && candidate.Light != theme.Light) ?? theme;

    /// <summary>True for six- or eight-digit hexadecimal color text without a leading '#'.</summary>
    public static bool IsHex(string? value, bool allowAlpha = true) =>
        value is { Length: 6 or 8 } && (allowAlpha || value.Length == 6) && value.All(Uri.IsHexDigit);

    /// <summary>The embedded theme files, each parsed as a user's file would be; a fault in one is a build fault, so it throws.</summary>
    private static List<ThemeDefinition> LoadBuiltIn()
    {
        var assembly = typeof(ThemeCatalog).Assembly;
        var themes = new List<ThemeDefinition>();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("The built-in theme " + name + " could not be opened.");
            using var reader = new StreamReader(stream);
            var theme = ThemeFiles.Parse(reader.ReadToEnd(), out var errors)
                ?? throw new InvalidDataException("The built-in theme " + name + " is not a valid theme file: " + string.Join("; ", errors));
            if (themes.Any(item => item.Id == theme.Id)) throw new InvalidDataException("Two built-in themes have the id " + theme.Id + ".");
            themes.Add(theme);
        }
        if (themes.Count == 0) throw new InvalidDataException("No built-in theme files are embedded in Sic.Core.");
        return themes;
    }
}
