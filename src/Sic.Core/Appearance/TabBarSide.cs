namespace Sic.Core.Appearance;

/// <summary>
/// Which edge the bars run along (Settings → Tabs). "top", the tab bar in the title-bar row, is where they have always been
/// and the default; "bottom" puts the tab bar above the status bar, each tab drawn upside down so the selected one still opens
/// into the panes, each pane's header under its terminal, and the window's own buttons at the strip's end, with no title bar
/// (docs/decisions/tab-bar-side.md). Kept as a word, as <see cref="SidebarSide"/> is, and normalized on load so an unknown
/// word leaves the bars at the top.
/// </summary>
public static class TabBarSide
{
    public const string Top = "top", Bottom = "bottom";
    public static IReadOnlyList<string> Choices { get; } = [Top, Bottom];

    public static string Normalize(string? side) => side?.Trim().ToLowerInvariant() == Bottom ? Bottom : Top;

    public static bool IsBottom(string? side) => Normalize(side) == Bottom;

    public static string Label(string? side) => IsBottom(side) ? "Bottom" : "Top";
}
