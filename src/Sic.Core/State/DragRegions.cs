namespace Sic.Core.State;

/// <summary>
/// The window's drag areas in the form Windows takes them: rectangles of physical pixels from the client area's top-left.
/// The chrome is measured in DIPs; <see cref="FromDips"/> scales an element's bounds, and <see cref="Subtract"/> cuts out
/// what must stay clickable, because a drag area takes every pointer event over it, hover and tooltips included.
/// </summary>
public static class DragRegions
{
    /// <summary>
    /// An element's bounds in DIPs as physical pixels. The edges are rounded rather than the size, so two areas that touch
    /// in DIPs still touch in pixels. A size that is not positive, as a band worked out from a layout squeezed past its
    /// content gives, is an empty rectangle rather than an error.
    /// </summary>
    public static PixelRect FromDips(double x, double y, double width, double height, double scale)
    {
        if (!(width > 0) || !(height > 0) || !(scale > 0) || !double.IsFinite(x) || !double.IsFinite(y)) return default;
        int left = (int)Math.Round(x * scale), top = (int)Math.Round(y * scale);
        int right = (int)Math.Round((x + width) * scale), bottom = (int)Math.Round((y + height) * scale);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : default;
    }

    /// <summary>The part of <paramref name="area"/> inside <paramref name="bounds"/>; empty when they do not overlap.</summary>
    public static PixelRect Intersect(PixelRect area, PixelRect bounds)
    {
        int left = Math.Max(area.X, bounds.X), top = Math.Max(area.Y, bounds.Y);
        int right = Math.Min(area.Right, bounds.Right), bottom = Math.Min(area.Bottom, bounds.Bottom);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : default;
    }

    /// <summary>
    /// What is left of <paramref name="area"/> once the <paramref name="holes"/> are cut out, as rectangles that do not overlap.
    /// The area is swept top to bottom in bands between the holes' edges, each band keeping the spans no hole covers. A span
    /// with the same left and right edges as a piece ending directly above it extends that piece, so buttons that touch leave
    /// one strip down each side; buttons with space between them, as on the rail, leave a pair of side pieces each, with a
    /// full-width band between pairs. Holes may overlap each other or reach outside the area.
    /// </summary>
    public static IReadOnlyList<PixelRect> Subtract(PixelRect area, IEnumerable<PixelRect> holes)
    {
        if (area.Width <= 0 || area.Height <= 0) return [];
        var cuts = holes.Select(hole => Intersect(hole, area)).Where(hole => hole.Width > 0).ToArray();
        var edges = cuts.SelectMany(cut => new[] { cut.Y, cut.Bottom }).Append(area.Y).Append(area.Bottom).Distinct().Order().ToArray();
        var pieces = new List<PixelRect>();
        for (var band = 0; band < edges.Length - 1; band++)
        {
            int top = edges[band], bottom = edges[band + 1], x = area.X;
            foreach (var cut in cuts.Where(cut => cut.Y <= top && cut.Bottom >= bottom).OrderBy(cut => cut.X))
            {
                if (cut.X > x) Keep(x, cut.X, top, bottom);
                x = Math.Max(x, cut.Right);
            }
            if (x < area.Right) Keep(x, area.Right, top, bottom);
        }
        return pieces;

        void Keep(int left, int right, int top, int bottom)
        {
            var above = pieces.FindIndex(piece => piece.X == left && piece.Right == right && piece.Bottom == top);
            if (above >= 0) pieces[above] = pieces[above] with { Height = bottom - pieces[above].Y };
            else pieces.Add(new(left, top, right - left, bottom - top));
        }
    }
}
