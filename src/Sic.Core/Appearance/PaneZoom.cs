using Sic.Core.State;

namespace Sic.Core.Appearance;

/// <summary>
/// The zoomed pane (Ctrl+Shift+Z): the focused pane of a tab lifted out of its tiling as a card over the other panes, which are
/// dimmed behind it, and put back by the same key. How big the card is (Settings → General → Zoomed pane): a share of the
/// display's work area, whatever the window's size, the window growing to the work area while the card needs the room; a share
/// of the window's body, the rail and the panel included; or of the terminal area alone, the tab's own room for its panes. The
/// share is a whole percent from <see cref="MinPercent"/> to <see cref="MaxPercent"/>, clamped on load; the frame is a word,
/// normalized on load as <see cref="TabBarSide"/> is, so an unknown word gives the default.
/// </summary>
public static class PaneZoom
{
    /// <summary>The frames the card's share is taken of: the display's work area, the window's body, or the terminal area alone.</summary>
    public const string Screen = "screen", Window = "window", Area = "area";
    public static IReadOnlyList<string> Choices { get; } = [Screen, Window, Area];
    public const string Default = Screen;

    /// <summary>
    /// The smallest, largest and default shares, in percent. The largest leaves a margin of at least 5% on every side, which
    /// keeps the card clear of the tab strip and the title bar's drag area above it in a window of any ordinary height.
    /// </summary>
    public const int MinPercent = 50, MaxPercent = 90, DefaultPercent = 80;

    public static string Normalize(string? frame) => frame?.Trim().ToLowerInvariant() switch
    {
        Window => Window,
        Area => Area,
        _ => Default
    };

    public static string Label(string? frame) => Normalize(frame) switch
    {
        Window => "Window",
        Area => "Terminal area",
        _ => "Screen"
    };

    public static int Clamp(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);

    public static string PercentLabel(int percent) => Clamp(percent) + "%";

    /// <summary>
    /// The card: <paramref name="percent"/> (clamped) of <paramref name="frame"/>'s width and of its height, centred in it, in
    /// whole pixels so the terminal's cells land on the same pixels each time. An empty frame gives an empty card.
    /// </summary>
    public static PaneRect Card(PaneRect frame, int percent)
    {
        var share = Clamp(percent) / 100.0;
        return Centre(frame, frame.Width * share, frame.Height * share);
    }

    /// <summary>
    /// The card for the Screen frame: <paramref name="percent"/> (clamped) of the work area's width and height, centred in
    /// <paramref name="frame"/> (the window's body) and no bigger than it, in whole pixels.
    /// </summary>
    public static PaneRect ScreenCard(PaneRect frame, double workWidth, double workHeight, int percent)
    {
        var share = Clamp(percent) / 100.0;
        return Centre(frame, workWidth * share, workHeight * share);
    }

    /// <summary>
    /// Whether a body of <paramref name="bodyWidth"/> × <paramref name="bodyHeight"/> holds a card of <paramref name="percent"/>
    /// of the work area centred in it with <paramref name="strip"/> to spare above and below, so the card covers neither the tab
    /// strip nor the title bar's drag area. A body that does not is grown to the work area for the Screen frame.
    /// </summary>
    public static bool ScreenCardFits(double bodyWidth, double bodyHeight, double workWidth, double workHeight, int percent, double strip)
    {
        var share = Clamp(percent) / 100.0;
        return Math.Round(workWidth * share) <= bodyWidth && Math.Round(workHeight * share) + 2 * strip <= bodyHeight;
    }

    /// <summary>A <paramref name="width"/> × <paramref name="height"/> rectangle centred in <paramref name="frame"/> and no bigger than it, in whole pixels.</summary>
    private static PaneRect Centre(PaneRect frame, double width, double height)
    {
        if (frame.Width <= 0 || frame.Height <= 0) return new PaneRect(frame.X, frame.Y, 0, 0);
        width = Math.Min(Math.Round(width), Math.Floor(frame.Width));
        height = Math.Min(Math.Round(height), Math.Floor(frame.Height));
        return new PaneRect(Math.Round(frame.X + (frame.Width - width) / 2), Math.Round(frame.Y + (frame.Height - height) / 2), width, height);
    }
}
