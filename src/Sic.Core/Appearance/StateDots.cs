namespace Sic.Core.Appearance;

/// <summary>
/// The fill of a status dot (a tab's, a pane header's, the status bar's) and the beat of a blinking one. A theme's success and
/// warning shades are chosen for text and borders, and a dot of 5 or 6 px shows little of a muted shade, so the dot keeps the
/// theme's hue and is made vivid (<see cref="Vivid"/>): on a dark surface lifted to at least <see cref="LightnessOnDark"/>, on a
/// light one held at or under <see cref="LightnessOnLight"/>, where a lighter dot would fade, and either way saturated to at
/// least <see cref="Saturation"/>. A grey has no hue to keep and comes back as it is. See docs/decisions/state-dots.md.
/// </summary>
public static class StateDots
{
    /// <summary>The least saturation of a dot, in HSL.</summary>
    public const double Saturation = 0.78;
    /// <summary>The least lightness of a dot on a dark surface.</summary>
    public const double LightnessOnDark = 0.58;
    /// <summary>The most lightness of a dot on a light surface.</summary>
    public const double LightnessOnLight = 0.40;
    /// <summary>A Your turn dot's opacity on the off beat of its blink: dimmed enough to read as a blink, not so far that the dot is gone.</summary>
    public const double BlinkDim = 0.3;
    /// <summary>Half a blink, the terminal cursor's cadence: xterm shows its cursor 600 ms and hides it 600 ms.</summary>
    public static readonly TimeSpan BlinkHalf = TimeSpan.FromMilliseconds(600);

    /// <summary>The color as a dot on a dark or a light surface: the same hue, vivid.</summary>
    public static (byte R, byte G, byte B) Vivid(byte r, byte g, byte b, bool onDark)
    {
        var (hue, saturation, lightness) = ToHsl(r, g, b);
        if (saturation < 0.05) return (r, g, b);
        saturation = Math.Max(saturation, Saturation);
        lightness = onDark ? Math.Max(lightness, LightnessOnDark) : Math.Min(lightness, LightnessOnLight);
        return FromHsl(hue, saturation, lightness);
    }

    /// <summary>Hue in degrees from 0 up to 360, saturation and lightness from 0 to 1; a grey has hue 0 and saturation 0.</summary>
    public static (double Hue, double Saturation, double Lightness) ToHsl(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        double max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2, range = max - min;
        if (range < 1e-9) return (0, 0, lightness);
        var saturation = Math.Min(1, range / (1 - Math.Abs(2 * lightness - 1)));
        var hue = max == red ? ((green - blue) / range % 6 + 6) % 6 : max == green ? (blue - red) / range + 2 : (red - green) / range + 4;
        return (hue * 60, saturation, lightness);
    }

    public static (byte R, byte G, byte B) FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var sector = (hue % 360 + 360) % 360 / 60;
        var second = chroma * (1 - Math.Abs(sector % 2 - 1));
        var floor = lightness - chroma / 2;
        var (red, green, blue) = sector switch
        {
            < 1 => (chroma, second, 0.0),
            < 2 => (second, chroma, 0.0),
            < 3 => (0.0, chroma, second),
            < 4 => (0.0, second, chroma),
            < 5 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second)
        };
        return (Channel(red + floor), Channel(green + floor), Channel(blue + floor));

        static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
