using Sic.Core.State;

namespace Sic.Core.Appearance;

/// <summary>
/// The zoomed pane (Ctrl+Shift+Z): the focused pane of a tab lifted out of its tiling as a card over the other panes, which are
/// dimmed behind it, and put back by the same key. How big the card is (Settings → General → Zoomed pane): a share of the
/// window's body, the rail and the panel included, or of the terminal area alone, the tab's own room for its panes. The share
/// is a whole percent from <see cref="MinPercent"/> to <see cref="MaxPercent"/>, clamped on load; the frame is a word,
/// normalized on load as <see cref="TabBarSide"/> is, so an unknown word gives the default.
/// </summary>
public static class PaneZoom
{
    /// <summary>The frames the card's share is taken of: the window's body, or the terminal area alone.</summary>
    public const string Window = "window", Area = "area";
    public static IReadOnlyList<string> Choices { get; } = [Window, Area];
    public const string Default = Window;

    /// <summary>
    /// The smallest, largest and default shares, in percent. The largest leaves a margin of at least 5% on every side, which
    /// keeps the card clear of the tab strip and the title bar's drag area above it in a window of any ordinary height.
    /// </summary>
    public const int MinPercent = 50, MaxPercent = 90, DefaultPercent = 80;

    public static string Normalize(string? frame) => frame?.Trim().ToLowerInvariant() switch
    {
        Area => Area,
        _ => Default
    };

    public static string Label(string? frame) => Normalize(frame) switch
    {
        Area => "Terminal area",
        _ => "Window"
    };

    public static int Clamp(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);

    public static string PercentLabel(int percent) => Clamp(percent) + "%";

    /// <summary>
    /// The card: <paramref name="percent"/> (clamped) of <paramref name="frame"/>'s width and of its height, centred in it, in
    /// whole pixels so the terminal's cells land on the same pixels each time. An empty frame gives an empty card.
    /// </summary>
    public static PaneRect Card(PaneRect frame, int percent)
    {
        if (frame.Width <= 0 || frame.Height <= 0) return new PaneRect(frame.X, frame.Y, 0, 0);
        var share = Clamp(percent) / 100.0;
        var width = Math.Round(frame.Width * share);
        var height = Math.Round(frame.Height * share);
        return new PaneRect(Math.Round(frame.X + (frame.Width - width) / 2), Math.Round(frame.Y + (frame.Height - height) / 2), width, height);
    }
}
