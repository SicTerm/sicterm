namespace Sic.Core.Appearance;

/// <summary>
/// How wide the accent border is that marks the pane taking the keyboard in a split tab (Settings → Appearance → Session Indicator
/// Width): whole pixels from 1 to 8, 2 by default. The border lies in the terminal page's margin around the cells, so no width
/// covers a cell or resizes a terminal (docs/decisions/pane-border.md). Kept as a number and clamped on load; 0.9.26 and 0.9.27
/// kept a word instead, which <see cref="FromWord"/> reads.
/// </summary>
public static class PaneBorder
{
    /// <summary>The narrowest, widest and default widths in DIPs. The widest is the page's margin above and below the cells (terminal.css); beside them it is 12 px.</summary>
    public const int Min = 1, Max = 8, Default = 2;

    public static int Clamp(int width) => Math.Clamp(width, Min, Max);

    /// <summary>
    /// A width as 0.9.26 and 0.9.27 saved it: "thick" was 3 px and stays so. "thin", their default, reads as today's default, as
    /// "medium" (2 px) and an unknown word do.
    /// </summary>
    public static int FromWord(string? word) => word?.Trim().ToLowerInvariant() == "thick" ? 3 : Default;

    public static string Label(int width) => Clamp(width) + " px";

    /// <summary>
    /// The border's color as kept (Settings → Appearance → Session Indicator Color, since October 18, 2026): null for the theme's
    /// accent, else a <see cref="TabColors"/> preset id, a theme color's id or an upper-case RRGGBB. Anything else, <see cref="TabColors.None"/>
    /// included, reads as the accent, so a hand-edited file cannot leave the border invisible.
    /// </summary>
    public static string? NormalizeColor(string? value) => value is null || value == TabColors.None ? null : TabColors.Normalize(value);

    /// <summary>
    /// The RRGGBB to paint for a kept color, or null for the theme's accent. A preset takes its dark-strip shade: the border lies
    /// over the terminal, which is dark. A theme color (<see cref="TabColors.ThemeRoles"/>, since November 12, 2026) takes the
    /// shade of <paramref name="theme"/>, or without one the standard preset of its hue.
    /// </summary>
    public static string? ResolveColor(string? value, ThemeDefinition? theme = null) => TabColors.Resolve(NormalizeColor(value), darkStrip: true, theme);
}
