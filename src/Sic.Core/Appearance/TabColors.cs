namespace Sic.Core.Appearance;

/// <summary>A named tab color with one shade for dark tab strips and a deeper one for light strips, both RRGGBB.</summary>
public sealed record TabColorPreset(string Id, string Name, string OnDark, string OnLight);

/// <summary>
/// Tab colors are stored as a preset id, which follows the strip between shades, as a theme color's id, which follows the
/// theme (<see cref="ThemeRoles"/>), or as an opaque RRGGBB custom color, which is painted exactly as chosen on every theme. Anything else reads as no color, so a hand-edited file cannot break a tab.
/// </summary>
public static class TabColors
{
    /// <summary>
    /// A tab's own "no color": kept like a color, so it stands over the color of the pinned folder the tab works in
    /// (<see cref="State.FolderColors"/>), and never painted.
    /// </summary>
    public const string None = "none";

    /// <summary>How many recently used custom colors are offered as extra swatches.</summary>
    public const int RecentLimit = 4;

    /// <summary>The swatches in grid order: four across, two rows.</summary>
    public static IReadOnlyList<TabColorPreset> Presets { get; } =
    [
        new("red", "Red", "F87171", "DC2626"),
        new("orange", "Orange", "FB923C", "EA580C"),
        new("yellow", "Yellow", "FACC15", "CA8A04"),
        new("green", "Green", "4ADE80", "16A34A"),
        new("cyan", "Cyan", "22D3EE", "0891B2"),
        new("blue", "Blue", "60A5FA", "2563EB"),
        new("purple", "Purple", "C084FC", "9333EA"),
        new("pink", "Pink", "F472B6", "DB2777")
    ];

    public static TabColorPreset? Preset(string? id) => Presets.FirstOrDefault(preset => preset.Id == id);

    /// <summary>What a theme color's id starts with; the rest is the id of the standard preset of the same hue.</summary>
    public const string ThemePrefix = "theme-";

    /// <summary>
    /// The current theme's own colors, in the standard swatches' order and under their names (since November 12, 2026): each is
    /// kept as its id, not as the color, so a tab or a folder given one takes the next theme's shade of it when the theme
    /// changes. The shades come from the hues every theme file already has, its terminal palette (<see cref="ThemeShade"/>).
    /// </summary>
    public static IReadOnlyList<TabColorPreset> ThemeRoles { get; } = [.. Presets.Select(preset => preset with { Id = ThemePrefix + preset.Id })];

    /// <summary>True for a theme color's id.</summary>
    public static bool IsThemeRole(string? value) => value is not null && value.StartsWith(ThemePrefix, StringComparison.Ordinal) && Preset(value[ThemePrefix.Length..]) is not null;

    /// <summary>True for an opaque RRGGBB custom color, the kind the recent swatches remember.</summary>
    public static bool IsCustom([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) => ThemeCatalog.IsHex(value, allowAlpha: false);

    /// <summary>
    /// The RRGGBB a theme has for a theme color's id, or null for another value: red, yellow, green, cyan, blue and purple are
    /// its terminal palette's; orange, which no palette names, is halfway between its red and its yellow, and pink halfway
    /// between its red and its purple, so both stay in the palette's character.
    /// </summary>
    public static string? ThemeShade(string? value, ThemeDefinition theme)
    {
        if (!IsThemeRole(value)) return null;
        var palette = theme.Terminal;
        return value![ThemePrefix.Length..] switch
        {
            "red" => palette.Red.ToUpperInvariant(),
            "orange" => Halfway(palette.Red, palette.Yellow),
            "yellow" => palette.Yellow.ToUpperInvariant(),
            "green" => palette.Green.ToUpperInvariant(),
            "cyan" => palette.Cyan.ToUpperInvariant(),
            "blue" => palette.Blue.ToUpperInvariant(),
            "purple" => palette.Magenta.ToUpperInvariant(),
            _ => Halfway(palette.Red, palette.Magenta)
        };
    }

    private static string Halfway(string a, string b)
    {
        static int Part(string hex, int at) => Convert.ToInt32(hex.Substring(at, 2), 16);
        return string.Concat(new[] { 0, 2, 4 }.Select(at => ((Part(a, at) + Part(b, at) + 1) / 2).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>True for a preset id, a theme color's id, an opaque RRGGBB custom color, or <see cref="None"/>.</summary>
    public static bool IsValid(string? value) => value == None || Preset(value) is not null || IsThemeRole(value) || ThemeCatalog.IsHex(value, allowAlpha: false);

    /// <summary>The stored form of a color: a preset id, a theme color's id or <see cref="None"/> as is, a custom color as upper-case RRGGBB; null when the value is not a color.</summary>
    public static string? Normalize(string? value) => value == None || Preset(value) is not null || IsThemeRole(value) ? value : ThemeCatalog.IsHex(value, allowAlpha: false) ? value!.ToUpperInvariant() : null;

    /// <summary>
    /// The RRGGBB to paint for a stored value on a dark or light strip, or null for no color, <see cref="None"/> or an
    /// unrecognized value. A theme color's id takes <paramref name="theme"/>'s shade; where no theme is given, the standard
    /// preset of the same hue.
    /// </summary>
    public static string? Resolve(string? value, bool darkStrip, ThemeDefinition? theme = null) =>
        IsThemeRole(value) ? (theme is null ? Resolve(value![ThemePrefix.Length..], darkStrip) : ThemeShade(value, theme)) :
        Preset(value) is { } preset ? (darkStrip ? preset.OnDark : preset.OnLight) : value == None ? null : Normalize(value);

    /// <summary>The recent custom colors with <paramref name="custom"/> moved to the front, without duplicates or invalid entries, capped at <see cref="RecentLimit"/>.</summary>
    public static List<string> Remember(IEnumerable<string>? recent, string custom)
    {
        var color = ThemeCatalog.IsHex(custom, allowAlpha: false) ? custom.ToUpperInvariant() : throw new ArgumentException("A custom tab color must be RRGGBB: " + custom, nameof(custom));
        return new[] { color }.Concat((recent ?? []).Where(item => ThemeCatalog.IsHex(item, allowAlpha: false)).Select(item => item.ToUpperInvariant()))
            .Distinct(StringComparer.Ordinal).Take(RecentLimit).ToList();
    }
}
