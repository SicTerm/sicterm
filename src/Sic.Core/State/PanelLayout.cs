namespace Sic.Core.State;

/// <summary>
/// How wide the rail's panel may be. The user drags its edge; the width stays between <see cref="MinWidth"/> (the session
/// list's rows and the Settings controls still fit) and <see cref="MaxWidth"/>, and never leaves the terminal area beside it
/// narrower than <see cref="MinTerminalWidth"/>.
/// </summary>
public static class PanelLayout
{
    public const int DefaultWidth = 320, MinWidth = 240, MaxWidth = 560, MinTerminalWidth = 400;

    /// <summary>
    /// The width to use for a requested one. <paramref name="sharedWidth"/> is the room the panel and the terminal area share
    /// (the window less the rail); unknown, as when settings load before the window is laid out, it is unbounded. When the
    /// window is too narrow for both floors the panel keeps its own minimum, since a panel narrower than that is no use.
    /// </summary>
    public static int ClampWidth(int requested, double sharedWidth = double.PositiveInfinity)
    {
        var ceiling = double.IsFinite(sharedWidth) ? Math.Min(MaxWidth, (int)Math.Floor(sharedWidth) - MinTerminalWidth) : MaxWidth;
        return Math.Max(MinWidth, Math.Min(requested, ceiling));
    }

    /// <summary>
    /// How much of the title-bar row the caption buttons (minimize, maximize, close) cover that the tab strip and the panel
    /// header must each keep clear, in DIPs. The buttons overlay the window's top-right corner, <paramref name="captionWidth"/>
    /// wide. With the rail on the left they sit over the end of the tab strip. With it on the right the rail takes the first
    /// <paramref name="railWidth"/> of them and the rest reaches over the panel header, and over the end of the tab strip for as
    /// much as the panel, <paramref name="panelWidth"/> wide where it is (sliding, or 0 when shut), leaves uncovered.
    /// </summary>
    public static (double Strip, double Header) CaptionReserve(bool right, double captionWidth, double railWidth, double panelWidth)
    {
        var width = double.IsFinite(captionWidth) ? Math.Max(0, captionWidth) : 0;
        if (!right) return (width, 0);
        var beyondRail = Math.Max(0, width - railWidth);
        return (Math.Max(0, beyondRail - (double.IsFinite(panelWidth) ? Math.Max(0, panelWidth) : 0)), beyondRail);
    }
}
