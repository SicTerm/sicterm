namespace Sic.Core.Appearance;

/// <summary>
/// What the window's chrome, the rail, the panel, the tab strip and the status bar, is made of (Settings → Chrome;
/// docs/decisions/window-transparency.md). "solid", the theme's own colors, is the default; "frosted" paints them part-clear
/// over a blurred view of what is behind the window; "clear" does the same with no blur, so what is behind can be read through
/// them. The terminals stay solid in all three. Kept as a word, as <see cref="TabBarSide"/> is, and normalized on load so an
/// unknown word leaves the chrome solid.
/// </summary>
public static class ChromeStyle
{
    public const string Solid = "solid", Frosted = "frosted", Clear = "clear";
    public static IReadOnlyList<string> Choices { get; } = [Solid, Frosted, Clear];

    public static string Normalize(string? style) => style?.Trim().ToLowerInvariant() switch
    {
        Frosted => Frosted,
        Clear => Clear,
        _ => Solid
    };

    /// <summary>Whether the chrome lets the window's backdrop show through it.</summary>
    public static bool SeeThrough(string? style) => Normalize(style) != Solid;

    public static string Label(string? style) => Normalize(style) switch
    {
        Frosted => "Frosted",
        Clear => "Clear",
        _ => "Solid"
    };
}
