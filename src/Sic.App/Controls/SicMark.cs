using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Sic.App.Controls;

/// <summary>
/// The SIC mark as XAML geometry, so the chrome never decodes a bitmap: since 0.9.15 a >_ prompt inside a circular arrow
/// that runs counter-clockwise round it and climbs into the _'s end (the owner's sketch, the icon since 0.9.4), with the
/// treatment scripts/Build-MarkDrawings.ps1 draws: the loop rises dark and faint from beneath the head and firms and
/// brightens as it comes round, lit from above, the _ casts a shadow on the head, and above 48 px the mark floats on a soft
/// shadow. It comes in two sets, for the surface it sits on: the white >_ and a cyan arrow on a dark one, as on sic.ico,
/// and the near-black >_ and a teal arrow on a light one, as on sic-light.ico. Where the mark's size on screen comes to the
/// size of one of the .ico's frames (16 to 48 px), it is that frame, drawn on its own pixel grid and shown 1:1, as the
/// taskbar shows it: the rail's 24 px mark is 24, 30, 36, 42 or 48 px at 100% to 200% scaling. Above 48 px it is the mark
/// on the 256 grid. The .ico's drawings use masks and blurs, which XAML lacks, so the chrome draws its own in filled
/// outlines and linear gradients (Assets/brand/chrome, baked into MarkGeometry.g.cs by scripts/Build-MarkGeometry.ps1).
/// The loop's faint tail is drawn as arcs laid over the colour of the surface behind the mark, which makes them opaque so
/// they overlap with no seam; Mica themes give their base colour, as the tab strip does. The surround stays transparent.
/// </summary>
internal static partial class SicMark
{
    private const int LargestFrame = 48;
    // The wordmark's faces, in em: each one's capital height.
    private const double SoraCap = 0.73, AzeretCap = 0.698;

    /// <summary>What a mark from <see cref="Create"/> was asked for and what it last drew.</summary>
    private sealed class MarkState(double size, Func<Windows.UI.Color> surface)
    {
        internal double Size { get; } = size;
        internal Func<Windows.UI.Color> Surface { get; } = surface;
        internal int Pixels { get; set; } = -1;
        internal Windows.UI.Color? Drawn { get; set; }
        internal bool? Light { get; set; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Viewbox, MarkState> States = new();
    private static readonly List<WeakReference<Viewbox>> Live = [];

    /// <summary>Whether a surface of this colour takes the light set (the near-black >_): above about 0.19 in luminance.</summary>
    internal static bool IsLightSurface(Windows.UI.Color color) => Sic.Core.Appearance.AppIcon.PrefersDarkInk(Theme.Luminance(color));

    /// <summary>
    /// Draws every mark again for the surface behind it, after a theme change: its <c>surface</c> is asked again, and a mark
    /// whose surface has not changed is left as it is.
    /// </summary>
    internal static void RedrawAll()
    {
        Live.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in Live) if (reference.TryGetTarget(out var box)) Draw(box);
    }

    /// <summary>Whether a mark from <see cref="Create"/> is drawing its light set; null for anything else.</summary>
    internal static bool? LightArt(object? mark) => mark is Viewbox box && States.TryGetValue(box, out var state) ? state.Light : null;

    /// <summary>
    /// One filled outline of the mark: a solid colour, or a gradient between two points on its grid, each colour RRGGBB or
    /// RRGGBBAA. A <paramref name="Surface"/> layer is laid over the surface's colour, which makes it opaque.
    /// </summary>
    private sealed record Layer(string Data, string? Solid, Gradient? Gradient, bool Surface);

    private sealed record Gradient(double X1, double Y1, double X2, double Y2, (double Offset, string Hex)[] Stops)
    {
        // Absolute, as the SVG's userSpaceOnUse, but a Path places an absolute brush from the top left of its outline's
        // bounds, not from the canvas, so the end points are moved by that corner, origin.
        internal LinearGradientBrush Brush(Windows.UI.Color? surface, Point origin)
        {
            var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(X1 - origin.X, Y1 - origin.Y), EndPoint = new Point(X2 - origin.X, Y2 - origin.Y) };
            foreach (var (offset, hex) in Stops) brush.GradientStops.Add(new GradientStop { Offset = offset, Color = ColorOf(hex, surface) });
            return brush;
        }
    }

    private static Windows.UI.Color ColorOf(string hex, Windows.UI.Color? surface) => surface is { } under ? Theme.Over(Theme.Parse(hex), under) : Theme.Parse(hex);

    /// <summary>
    /// The mark, <paramref name="size"/> device-independent pixels square: the frame of that size on screen where there is
    /// one, the nearest frame scaled at any other size up to 48 px, and the mark on the 256 grid above that, for the surface
    /// behind it (<paramref name="surface"/>: its colour, asked on every draw, which picks the set and what the loop's faint
    /// tail is laid over). Drawn again when the window moves to a display with other scaling, and by <see cref="RedrawAll"/>
    /// after a theme change.
    /// </summary>
    internal static Viewbox Create(double size, Func<Windows.UI.Color> surface)
    {
        var box = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        States.Add(box, new MarkState(size, surface));
        Live.Add(new WeakReference<Viewbox>(box));
        XamlRoot? watched = null;
        void Changed(XamlRoot root, XamlRootChangedEventArgs args) => Draw(box);
        box.Loaded += (_, _) =>
        {
            Draw(box);
            if (watched is null && box.XamlRoot is { } root) { watched = root; root.Changed += Changed; }
        };
        box.Unloaded += (_, _) => { if (watched is { } root) { root.Changed -= Changed; watched = null; } };
        Draw(box);
        return box;
    }

    /// <summary>The size of the .ico frame a mark from <see cref="Create"/> is showing, or 0 while it shows the mark above 48 px.</summary>
    internal static int FrameSize(object? mark) => mark is Viewbox { Child: Canvas { Tag: int size } } ? size : 0;

    private static void Draw(Viewbox box)
    {
        if (!States.TryGetValue(box, out var state)) return;
        var pixels = (int)Math.Round(state.Size * (box.XamlRoot?.RasterizationScale ?? 1));
        var surface = state.Surface();
        if (state.Pixels == pixels && state.Drawn == surface) return;
        var light = IsLightSurface(surface);
        state.Pixels = pixels; state.Drawn = surface; state.Light = light;
        if (pixels > LargestFrame) { box.Child = Paint(light ? FullCutLight : FullCut, 256, surface); return; }
        var frames = light ? FramesLight : Frames;
        var frame = frames.Keys.OrderBy(key => Math.Abs(key - pixels)).ThenByDescending(key => key).First();
        var canvas = Paint(frames[frame], frame, surface);
        canvas.Tag = frame;
        box.Child = canvas;
    }

    private static Canvas Paint(Layer[] layers, double grid, Windows.UI.Color surface)
    {
        var canvas = new Canvas { Width = grid, Height = grid };
        foreach (var layer in layers)
        {
            Windows.UI.Color? under = layer.Surface ? surface : null;
            var outline = Geometry(layer.Data);
            var corner = new Point(outline.Bounds.X, outline.Bounds.Y);
            canvas.Children.Add(new Path { Data = outline, Fill = layer.Gradient is { } gradient ? gradient.Brush(under, corner) : new SolidColorBrush(ColorOf(layer.Solid!, under)) });
        }
        return canvas;
    }

    /// <summary>
    /// The wordmark: "Sic" in Sora ExtraBold and "Term" in Azeret Mono Light, in the accent colour (until October 17, 2026, both
    /// in the theme's display face; until October 18, with the snapback cap perched on its "T", docs/decisions/wordmark.md). It
    /// keeps these shipped faces in every theme, as the mark keeps its shape. <paramref name="size"/> is the size of "Sic";
    /// "Term" is sized so both stand the same capital height, and once both are measured it is laid on Sic's baseline.
    /// </summary>
    internal static FrameworkElement Wordmark(Theme theme, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var sic = theme.Heading("Sic", size); sic.FontFamily = Theme.Sora; sic.FontWeight = new Windows.UI.Text.FontWeight { Weight = 800 };
        sic.TextTrimming = TextTrimming.None; sic.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(sic);
        var term = theme.Heading("Term", size * SoraCap / AzeretCap); term.FontFamily = Theme.AzeretMono; term.FontWeight = new Windows.UI.Text.FontWeight { Weight = 300 };
        term.Foreground = theme.Accent; term.TextTrimming = TextTrimming.None; term.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(term);
        void Align(object sender, SizeChangedEventArgs args)
        {
            if (sic.BaselineOffset <= 0 || term.BaselineOffset <= 0) return;
            var shift = sic.BaselineOffset - term.BaselineOffset;
            sic.Margin = new Thickness(0, Math.Max(0, -shift), 0, 0);
            term.Margin = new Thickness(0, Math.Max(0, shift), 0, 0);
        }
        sic.SizeChanged += Align; term.SizeChanged += Align;
        return row;
    }

    private static Microsoft.UI.Xaml.Media.Geometry Geometry(string data) =>
        (Microsoft.UI.Xaml.Media.Geometry)XamlBindingHelper.ConvertValue(typeof(Microsoft.UI.Xaml.Media.Geometry), data);
}
