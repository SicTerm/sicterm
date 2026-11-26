namespace Sic.Core.Appearance;

/// <summary>
/// Which side of the window the rail and its panel sit on (Settings → Appearance → Side Panel, Sidebar until October 15, 2026). "left" is where they have always been and the default; "right" puts
/// the terminals against the window's left edge. Kept as a word so a settings file reads plainly, and normalized on load so an
/// unknown word leaves the rail on the left.
/// </summary>
public static class SidebarSide
{
    public const string Left = "left", Right = "right";
    public static IReadOnlyList<string> Choices { get; } = [Left, Right];

    public static string Normalize(string? side) => side?.Trim().ToLowerInvariant() == Right ? Right : Left;

    public static bool IsRight(string? side) => Normalize(side) == Right;

    public static string Label(string? side) => IsRight(side) ? "Right" : "Left";
}
