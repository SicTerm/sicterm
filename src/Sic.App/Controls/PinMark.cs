using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Sic.App.Controls;

/// <summary>
/// The pinned mark: a filled push pin ending in the same sharp point as the outline glyph (Segoe Fluent Icons E840) that a row
/// shows under the pointer. Segoe's own filled pins (E841, E842) end in a stub, so the pinned state is drawn from the Fluent UI
/// System Icons "Pin 16 Filled" path (MIT), the family the outline glyph is cut from, in a live brush so it follows the theme.
/// </summary>
internal static class PinMark
{
    private const string PathData = "M10.0589 2.44535C9.34701 1.73087 8.14697 1.90854 7.67261 2.79864L5.6526 6.58902L2.8419 7.52592C2.6775 7.58072 2.5532 7.71673 2.51339 7.88539"
        + "C2.47357 8.05404 2.52392 8.23128 2.64646 8.35382L4.79287 10.5002L2.14637 13.1467L2 14.0003L2.85348 13.8538L5.49998 11.2073L7.64646 13.3538"
        + "C7.76899 13.4764 7.94623 13.5267 8.11489 13.4869C8.28354 13.4471 8.41955 13.3228 8.47435 13.1584L9.41143 10.3472L13.1897 8.32448"
        + "C14.0759 7.85006 14.2538 6.65535 13.5443 5.9433L10.0589 2.44535Z";

    internal static Viewbox Create(Brush fill, double size) => new()
    {
        Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Child = new Path { Width = 16, Height = 16, Fill = fill, Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), PathData) }
    };
}
