using System.Globalization;

namespace Sic.Core.Appearance;

/// <summary>
/// The colours of the status bar's usage-limit meters (docs/decisions/usage-limits.md), worked out from any theme rather than
/// chosen per theme, since anyone can add a theme file. The bar is the theme's rail over its base. Each state keeps the theme's
/// own success, warning or danger hue, made vivid as a dot is (<see cref="StateDots.Vivid"/>), then moved away from the bar in
/// lightness until it reaches <see cref="MeterContrast"/>, the WCAG 1.4.11 minimum for a graphic: the dot rule alone leaves
/// several light themes' meters under it. The bar's ink is lifted the same way to <see cref="TextContrast"/>. When two of the
/// three hues are closer than <see cref="MinHueSeparation"/>, or one is grey, the meters could not tell the states apart, so
/// a standard green, amber and red is used instead, with the same floor.
/// </summary>
public static class LimitColors
{
    public const double MeterContrast = 3.0;
    public const double TextContrast = 4.5;
    /// <summary>Degrees of hue below which two state colours are taken as the same colour on a 4 px meter.</summary>
    public const double MinHueSeparation = 25;
    /// <summary>Saturation under which a colour is taken as grey, as <see cref="StateDots.Vivid"/> takes it.</summary>
    public const double GreySaturation = 0.05;
    private static readonly string[] StandardStates = ["2E9E4F", "E0A526", "E5342B"];

    /// <summary>The meter and text colours for one theme, as RRGGBB, with the bar they were made for.</summary>
    public sealed record Palette(string Surface, bool Dark, string Ink, string OnPace, string Ahead, string Near, bool Standard);

    public static Palette For(ThemeDefinition theme)
    {
        var surface = Over(theme.Rail, theme.Base);
        var dark = ThemeFiles.Luminance(surface) < 0.4;
        var ink = Floor(Over(theme.RailText, surface), surface, TextContrast);
        string[] states = [theme.Success, theme.Warning, theme.Danger];
        var standard = !Distinct(states);
        var chosen = (standard ? StandardStates : states).Select(color => Floor(Vivid(color, dark), surface, MeterContrast)).ToArray();
        return new Palette(surface, dark, ink, chosen[0], chosen[1], chosen[2], standard);
    }

    /// <summary>Whether the three state colours have a hue each, at least <see cref="MinHueSeparation"/> apart from one another.</summary>
    public static bool Distinct(IReadOnlyList<string> colors)
    {
        var hues = new List<double>();
        foreach (var color in colors)
        {
            var (r, g, b) = Rgb(color);
            var (hue, saturation, _) = StateDots.ToHsl(r, g, b);
            if (saturation < GreySaturation) return false;
            hues.Add(hue);
        }
        for (var i = 0; i < hues.Count; i++)
            for (var j = i + 1; j < hues.Count; j++)
            {
                var gap = Math.Abs(hues[i] - hues[j]) % 360;
                if (Math.Min(gap, 360 - gap) < MinHueSeparation) return false;
            }
        return true;
    }

    /// <summary>
    /// The colour with its hue and saturation kept and its lightness moved away from <paramref name="surface"/> (up on a dark
    /// one, down on a light one) in small steps until it reaches <paramref name="target"/> contrast, or as far as it can go.
    /// </summary>
    public static string Floor(string color, string surface, double target)
    {
        var dark = ThemeFiles.Luminance(surface) < 0.4;
        var away = Toward(color, surface, target, dark ? 0.01 : -0.01);
        if (Contrast(away, surface) >= target) return away;
        // A mid-tone bar may not leave room on its own side: the other way, when that reads better.
        var back = Toward(color, surface, target, dark ? -0.01 : 0.01);
        return Contrast(back, surface) > Contrast(away, surface) ? back : away;
    }

    private static string Toward(string color, string surface, double target, double step)
    {
        var (r, g, b) = Rgb(color);
        var (hue, saturation, lightness) = StateDots.ToHsl(r, g, b);
        var current = Hex(r, g, b);
        for (var count = 0; count < 100 && Contrast(current, surface) < target; count++)
        {
            lightness = Math.Clamp(lightness + step, 0, 1);
            var (nr, ng, nb) = StateDots.FromHsl(hue, saturation, lightness);
            current = Hex(nr, ng, nb);
        }
        return current;
    }

    /// <summary>The WCAG contrast ratio between two opaque RRGGBB colours.</summary>
    public static double Contrast(string a, string b)
    {
        double x = ThemeFiles.Luminance(a), y = ThemeFiles.Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>An RRGGBB or RRGGBBAA colour laid over an opaque one, as the window composites the rail over the base.</summary>
    public static string Over(string top, string bottom)
    {
        var (tr, tg, tb) = Rgb(top);
        var (br, bg, bb) = Rgb(bottom);
        var alpha = top.Length == 8 ? int.Parse(top.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 : 1;
        static byte Mix(byte over, byte under, double alpha) => (byte)Math.Round(under + (over - under) * alpha);
        return Hex(Mix(tr, br, alpha), Mix(tg, bg, alpha), Mix(tb, bb, alpha));
    }

    private static string Vivid(string color, bool dark)
    {
        var (r, g, b) = Rgb(color);
        var (vr, vg, vb) = StateDots.Vivid(r, g, b, dark);
        return Hex(vr, vg, vb);
    }

    private static (byte R, byte G, byte B) Rgb(string color) => (Channel(color, 0), Channel(color, 2), Channel(color, 4));
    private static byte Channel(string color, int offset) => byte.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static string Hex(byte r, byte g, byte b) => $"{r:X2}{g:X2}{b:X2}";
}
