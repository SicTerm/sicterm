namespace Sic.Core.Alerts;

/// <summary>A built-in sound as numbers: its id and name, its peak as a share of full scale, and its wave as a function of the time in seconds.</summary>
public sealed record SoundRecipe(string Id, string Name, double Peak, Func<double, double> Wave);

/// <summary>
/// The built-in completion sounds, made in code: no audio file ships (docs/decisions/ready-chime.md), and these are the chime,
/// ten simple sounds and ten fun ones that Chris chose from a listening round on October 9, 2026
/// (docs/decisions/completion-sounds.md). Each is sine partials with exponential decays under a raised-cosine rise, the fun ones
/// with soft square and saw waves, pitch glides and vibrato. A recipe is rendered until it has fallen 40 dB under its own peak and
/// then faded over 60 ms, so its length is what its decays give (fixed lengths were heard as cut short), as 16-bit mono PCM at
/// 44.1 kHz.
/// </summary>
public static class SoundRecipes
{
    public const int SampleRate = 44_100;
    /// <summary>The longest a recipe renders; every recipe rings out well inside it.</summary>
    public const double Longest = 3.0;
    /// <summary>The fade at the end, from where the sound has rung out to nothing, so no sound ends in a click.</summary>
    public const double Fade = 0.06;
    /// <summary>How far under its peak a sound counts as rung out, in decibels.</summary>
    public const double RungOut = -40;

    // Pitches: A3 220; E4 329.63; A4 440; G5 783.99; B5 987.77; C5 523.25; E5 659.26; C6 1046.5; D6 1174.66; E6 1318.51; G6 1567.98;
    // A6 1760; C7 2093; D7 2349.32; E7 2637.02; G7 3135.96; C8 4186.
    public static IReadOnlyList<SoundRecipe> All { get; } =
    [
        new("chime", "Chime", ReadyChime.Peak, t => Mallet(659.255, t, 0, 0.28) + 0.85 * Mallet(880, t, 0.12, 0.28)),

        // Simple, soft to piercing.
        new("breath", "Breath", 0.12, t => Rise(t, 0.06) * (Decay(t, 0.18) * Sine(220, t) + 0.6 * Decay(t, 0.16) * Sine(330, t) + 0.35 * Decay(t, 0.14) * Sine(440, t))),
        new("wood", "Wood", 0.2, t => 0.6 * Rise(t, 0.001) * Decay(t, 0.006) * Noise(t) + Decay(t, 0.02) * Sine(1150, t) + 0.5 * Decay(t, 0.012) * Sine(2300, t)),
        new("drop", "Drop", 0.22, t => Rise(t, 0.004) * (Decay(t, 0.2) * Sine(440, t) + 0.35 * Decay(t, 0.07) * Sine(1760, t) + 0.1 * Decay(t, 0.03) * Sine(4400, t))),
        new("pluck", "Pluck", 0.25, t => Pluck(329.63, t, 0, 0.32)),
        new("bell", "Bell", 0.3, t => Rise(t, 0.003) * (Decay(t, 0.45) * Sine(1046.5, t) + 0.3 * Decay(t, 0.6) * Sine(523.25, t) + 0.5 * Decay(t, 0.3) * Sine(1046.5 * 2.76, t) + 0.25 * Decay(t, 0.15) * Sine(1046.5 * 5.4, t))),
        new("two-note", "Two-note", 0.3, t => Mallet(783.99, t, 0, 0.2) + 0.9 * Mallet(1174.66, t, 0.09, 0.2)),
        new("ding", "Ding", 0.35, t => Rise(t, 0.003) * (Decay(t, 0.3) * Sine(1318.51, t) + 0.3 * Decay(t, 0.15) * Sine(2637.02, t) + 0.15 * Decay(t, 0.08) * Sine(3955.53, t))),
        new("glass", "Glass", 0.4, t => Rise(t, 0.002) * (Decay(t, 0.25) * Sine(1760, t) + 0.7 * Decay(t, 0.22) * Sine(2637.02, t) + 0.5 * Decay(t, 0.2) * Sine(3520, t))),
        new("ping", "Ping", 0.5, t => Rise(t, 0.001) * (Decay(t, 0.12) * Sine(2960, t) + 0.3 * Decay(t, 0.06) * Sine(5920, t))),
        new("pierce", "Pierce", 0.7, t => Gate(t, 0.06) * (Sine(4186, t) + 0.3 * Sine(12558, t)) + Gate(t - 0.1, 0.06) * (Sine(4186, t) + 0.3 * Sine(12558, t))),

        // Fun, soft to piercing.
        new("pop", "Pop", 0.3, t => Rise(t, 0.001) * Decay(t, 0.045) * At(Glide(400, 120, 0.04, t))),
        new("bubble", "Bubble", 0.3, t => Rise(t, 0.003) * Decay(t, 0.09) * At(Glide(300, 900, 0.12, t))),
        new("drip", "Drip", 0.3, t => Rise(t, 0.001) * Decay(t, 0.03) * At(Glide(1800, 600, 0.03, t))
            + 0.6 * Rise(t - 0.08, 0.003) * Decay(t - 0.08, 0.07) * At(Glide(500, 1100, 0.08, t - 0.08))),
        new("boing", "Boing", 0.35, t => Rise(t, 0.004) * Decay(t, 0.25) * (At(Glide(330, 200, 0.4, t) + 0.22 * Decay(t, 0.2) * Math.Sin(Tau * 18 * t)) + 0.12 * At(3 * Glide(330, 200, 0.4, t)))),
        new("coin", "Coin", 0.35, t => 0.8 * Gate(t, 0.08) * SoftSquare(987.77 * t) + Rise(t - 0.08, 0.002) * Decay(t - 0.08, 0.22) * SoftSquare(1318.51 * (t - 0.08))),
        new("power-up", "Power-up", 0.35, t => Gate(t, 0.05) * SoftSquare(523.25 * t) + Gate(t - 0.05, 0.05) * SoftSquare(659.26 * t) + Gate(t - 0.1, 0.05) * SoftSquare(783.99 * t)
            + Gate(t - 0.15, 0.05) * SoftSquare(1046.5 * t) + Rise(t - 0.2, 0.002) * Decay(t - 0.2, 0.15) * SoftSquare(1318.51 * t)),
        new("pew", "Pew", 0.4, t => Rise(t, 0.002) * Decay(t, 0.1) * SoftSaw(Glide(1800, 200, 0.25, t))),
        new("robot", "Robot", 0.4, t => Gate(t, 0.06) * SoftSquare(880 * t) + Gate(t - 0.08, 0.06) * SoftSquare(1318.51 * t) + Gate(t - 0.16, 0.06) * SoftSquare(659.26 * t)),
        new("sparkle", "Sparkle", 0.4, t => Rise(t, 0.002) * Decay(t, 0.18) * Sine(1567.98, t) + Rise(t - 0.035, 0.002) * Decay(t - 0.035, 0.18) * Sine(1760, t)
            + Rise(t - 0.07, 0.002) * Decay(t - 0.07, 0.18) * Sine(2093, t) + Rise(t - 0.105, 0.002) * Decay(t - 0.105, 0.18) * Sine(2349.32, t)
            + Rise(t - 0.14, 0.002) * Decay(t - 0.14, 0.18) * Sine(2637.02, t) + Rise(t - 0.175, 0.002) * Decay(t - 0.175, 0.18) * Sine(3135.96, t)),
        new("tada", "Tada", 0.5, t => Gate(t, 0.08) * Brass(392, t, 0, 1) + Gate(t - 0.08, 0.08) * Brass(523.25, t, 0.08, 1)
            + 0.5 * (Brass(523.25, t, 0.16, 0.3) + Brass(659.26, t, 0.16, 0.3) + Brass(783.99, t, 0.16, 0.3) + Brass(1046.5, t, 0.16, 0.3)))
    ];

    /// <summary>The chime SIC rang from the start: two soft mallet notes rising a fourth, E5 then A5 120 ms later.</summary>
    public static SoundRecipe Chime => All[0];

    /// <summary>The recipe's samples in [-1, 1]: rendered up to <see cref="Longest"/>, cut where it has rung out, and faded.</summary>
    public static double[] Samples(SoundRecipe recipe)
    {
        var count = (int)Math.Round(Longest * SampleRate);
        var samples = new double[count];
        for (var index = 0; index < count; index++) samples[index] = recipe.Wave(index / (double)SampleRate);
        var loudest = 0.0;
        foreach (var sample in samples) loudest = Math.Max(loudest, Math.Abs(sample));
        var floor = loudest * Math.Pow(10, RungOut / 20);
        var last = count - 1;
        while (last > 0 && Math.Abs(samples[last]) < floor) last--;
        var fadeSamples = (int)Math.Round(Fade * SampleRate);
        count = Math.Min(count, last + 1 + fadeSamples);
        Array.Resize(ref samples, count);
        var fadeFrom = count - fadeSamples;
        for (var index = fadeFrom; index < count; index++) samples[index] *= 0.5 + 0.5 * Math.Cos(Math.PI * (index - fadeFrom) / (count - 1 - fadeFrom));
        return samples;
    }

    /// <summary>The recipe as a WAV image at its own peak, or at <paramref name="peak"/>: 0 gives the same length of silence.</summary>
    public static byte[] Render(SoundRecipe recipe, double? peak = null) => WavImage.Write(WavImage.Scale(Samples(recipe), peak ?? recipe.Peak), SampleRate, 1);

    public static Sound ToSound(SoundRecipe recipe) => new(recipe.Id, recipe.Name, builtIn: true, cut: false, () => Render(recipe));

    // The primitives, as the script has them.
    private const double Tau = 2 * Math.PI;
    private static double Sine(double frequency, double t) => Math.Sin(Tau * frequency * t);
    private static double At(double cycles) => Math.Sin(Tau * cycles);
    /// <summary>A square or a saw with a few harmonics only, so they bite without the hiss of a hard edge.</summary>
    private static double SoftSquare(double cycles) { var value = 0.0; for (var k = 1; k <= 9; k += 2) value += At(k * cycles) / k; return value; }
    private static double SoftSaw(double cycles) { var value = 0.0; for (var k = 1; k <= 6; k++) value += At(k * cycles) / k; return value; }
    private static double Decay(double t, double tau) => t < 0 ? 0 : Math.Exp(-t / tau);
    private static double Rise(double t, double rise) => t < 0 ? 0 : t < rise ? 0.5 - 0.5 * Math.Cos(Math.PI * t / rise) : 1;
    /// <summary>On for a length, with 4 ms edges: the shape of a bleep.</summary>
    private static double Gate(double t, double length)
    {
        const double edge = 0.004;
        if (t < 0 || t >= length) return 0;
        if (t < edge) return t / edge;
        if (t > length - edge) return (length - t) / edge;
        return 1;
    }
    /// <summary>The phase, in cycles, of a pitch that glides exponentially from f0 to f1 over d seconds and then holds.</summary>
    private static double Glide(double f0, double f1, double d, double t)
    {
        if (t <= 0) return 0;
        if (f0 == f1) return f0 * t;
        var k = Math.Log(f1 / f0) / d;
        if (t < d) return f0 * (Math.Exp(k * t) - 1) / k;
        return f0 * (Math.Exp(k * d) - 1) / k + f1 * (t - d);
    }
    /// <summary>The chime's mallet: the pitch, a faint octave, and a trace two octaves up that is gone within a tenth of a second.</summary>
    private static double Mallet(double frequency, double t, double onset, double decay)
    {
        t -= onset; if (t < 0) return 0;
        return Rise(t, 0.010) * (Decay(t, decay) * Sine(frequency, t) + 0.1 * Decay(t, decay / 2) * Sine(2 * frequency, t) + 0.03 * Decay(t, 0.04) * Sine(4 * frequency, t));
    }
    /// <summary>A plucked string: harmonics at 1/n, the higher ones dying sooner.</summary>
    private static double Pluck(double frequency, double t, double onset, double decay)
    {
        t -= onset; if (t < 0) return 0;
        var value = 0.0; for (var n = 1; n <= 7; n++) value += Decay(t, decay / n) * Sine(frequency * n, t) / n;
        return Rise(t, 0.002) * value;
    }
    /// <summary>A brassy note: harmonics at 1/n, all ringing together.</summary>
    private static double Brass(double frequency, double t, double onset, double decay)
    {
        t -= onset; if (t < 0) return 0;
        var value = 0.0; for (var n = 1; n <= 5; n++) value += Sine(frequency * n, t) / n;
        return Rise(t, 0.01) * Decay(t, decay) * value;
    }
    /// <summary>White noise that depends on the time alone, so a render is the same every time.</summary>
    private static double Noise(double t) { var x = Math.Sin(t * 1e5 + 0.1) * 43758.5453; return 2 * (x - Math.Floor(x)) - 1; }
}
