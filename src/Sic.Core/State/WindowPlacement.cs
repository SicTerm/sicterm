namespace Sic.Core.State;

/// <summary>A rectangle in physical pixels: of the screen for <c>AppWindow</c> and <c>DisplayArea</c>, of the client area for the window's drag areas.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Where the window was left, in physical pixels. While <see cref="Maximized"/> is set the bounds are the ones it restores
/// to, not the maximized ones, so un-maximizing after a restart lands where the window was before it was maximized.
/// </summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    /// <summary>How much of a saved window must lie on the display for it to stay where it was: enough to see it and grab it.</summary>
    public const int MinVisibleWidth = 200, MinVisibleHeight = 120;

    /// <summary>
    /// The saved placement made safe for the display it lands on. A window that still shows at least 200×120 px of itself
    /// stays put; one that does not (its display was detached, or the layout changed) is replaced by <paramref name="fallback"/>'s
    /// size centred on the work area. A size larger than the work area shrinks to it and is pulled inside, and the top edge is
    /// never left above the work area, because the title-bar row up there is where the window is usually grabbed. The maximized flag
    /// always survives.
    /// </summary>
    public static WindowPlacement Fit(WindowPlacement saved, PixelRect workArea, WindowPlacement fallback)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return fallback with { Maximized = saved.Maximized };
        if (saved.Width <= 0 || saved.Height <= 0 || !ShowsEnough(saved, workArea)) return Centre(fallback, workArea, saved.Maximized);
        var width = Math.Min(saved.Width, workArea.Width);
        var height = Math.Min(saved.Height, workArea.Height);
        var x = width < saved.Width ? Math.Clamp(saved.X, workArea.X, workArea.Right - width) : saved.X;
        var y = height < saved.Height ? Math.Clamp(saved.Y, workArea.Y, workArea.Bottom - height) : saved.Y;
        return new(x, Math.Max(y, workArea.Y), width, height, saved.Maximized);
    }

    private static bool ShowsEnough(WindowPlacement saved, PixelRect workArea)
    {
        var visibleWidth = Math.Min(saved.X + saved.Width, workArea.Right) - Math.Max(saved.X, workArea.X);
        var visibleHeight = Math.Min(saved.Y + saved.Height, workArea.Bottom) - Math.Max(saved.Y, workArea.Y);
        // A window smaller than the minimum only has to be wholly on the display.
        return visibleWidth >= Math.Min(MinVisibleWidth, saved.Width) && visibleHeight >= Math.Min(MinVisibleHeight, saved.Height);
    }

    private static WindowPlacement Centre(WindowPlacement fallback, PixelRect workArea, bool maximized)
    {
        var width = Math.Clamp(fallback.Width, 1, workArea.Width);
        var height = Math.Clamp(fallback.Height, 1, workArea.Height);
        return new(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height, maximized);
    }
}
