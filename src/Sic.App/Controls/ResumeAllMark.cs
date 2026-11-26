using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Sic.App.Controls;

/// <summary>
/// The Resume all glyph: the SIC mark reduced to one colour, a ">_" inside a circle that runs counter-clockwise round it and
/// climbs into the "_"'s end, as on the icon since 0.9.4. It stands in for the generic play glyph on the rail's Resume all
/// button and on the tab menu's Resume all tabs. Built by scripts/Build-MarkGeometry.ps1 on a 24-unit grid, redrawn at 16 units for menus (both in
/// MarkGeometry.g.cs); docs/brand/resume-all.svg is the same outline.
/// </summary>
internal static partial class ResumeAllMark
{
    /// <summary>The glyph at <paramref name="size"/>; its <see cref="Path"/> is the Viewbox's child, so the caller can recolour it.</summary>
    internal static Viewbox Create(Brush fill, double size) => new()
    {
        Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Child = new Path { Width = Grid, Height = Grid, Fill = fill, Data = Geometry(PathData) }
    };

    /// <summary>For a menu item: a PathIcon draws the 16-unit outline at its own size in the item's foreground, disabled state included.</summary>
    internal static PathIcon Icon() => new() { Data = Geometry(MenuPathData) };

    private static Geometry Geometry(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
}
