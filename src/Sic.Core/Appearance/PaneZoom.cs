using Sic.Core.State;

namespace Sic.Core.Appearance;

/// <summary>
/// The zoomed pane (Ctrl+Shift+Z): the focused pane lifted out of its tab's tiling as a card over the other panes, which are
/// dimmed behind it, until the same key puts it back. The card is a share of the terminal area (Settings → General → Zoomed
/// pane size), a whole percent from <see cref="MinPercent"/> to <see cref="MaxPercent"/>, clamped on load; a window made full
/// screen gives it more room.
/// </summary>
public static class PaneZoom
{
    /// <summary>The smallest, largest and default shares, in percent. The largest leaves a margin of 5% on every side, so the dimmed panes still show round the card.</summary>
    public const int MinPercent = 50, MaxPercent = 90, DefaultPercent = 80;

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
        var width = Math.Min(Math.Round(frame.Width * share), Math.Floor(frame.Width));
        var height = Math.Min(Math.Round(frame.Height * share), Math.Floor(frame.Height));
        return new PaneRect(Math.Round(frame.X + (frame.Width - width) / 2), Math.Round(frame.Y + (frame.Height - height) / 2), width, height);
    }
}
