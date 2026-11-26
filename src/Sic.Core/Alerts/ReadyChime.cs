namespace Sic.Core.Alerts;

/// <summary>
/// The chime that says an agent is ready for you (Settings → Alerts), and how often any completion sound may ring. It is the
/// first of the built-in sounds (<see cref="SoundRecipes.Chime"/>): two soft mallet notes rising a fourth, E5 then A5 120 ms
/// later, each a sine with a faint octave and a fainter bright partial that die away faster than it does, under a 10 ms rise.
/// Since October 9, 2026 it rings out until it is 40 dB under its peak, 1.48 s in all, where before it was cut at 0.72 s under a
/// 140 ms fade, which was heard as cut short. It peaks at <see cref="Peak"/> of full scale, about −10 dBFS, where Windows' own
/// sounds peak near full scale, and is 16-bit mono PCM at 44.1 kHz in a WAV image that PlaySound can take from memory. Any other
/// sound, a built-in recipe or a file of the user's own, rings in its place (<see cref="SoundCatalog"/>).
/// </summary>
public static class ReadyChime
{
    public const int SampleRate = SoundRecipes.SampleRate;
    /// <summary>The loudest sample as a share of full scale; the level a user's file is brought to as well.</summary>
    public const double Peak = 0.3;
    private static readonly Lazy<TimeSpan> RenderedLength = new(() => TimeSpan.FromSeconds(WavImage.Seconds(Render())));
    /// <summary>How long the chime plays, as its recipe rings out.</summary>
    public static TimeSpan Length => RenderedLength.Value;
    /// <summary>The least time between two sounds, so agents that finish together ring once.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(2);

    /// <summary>Whether a sound may ring now, the last one having rung at <paramref name="last"/>.</summary>
    public static bool MayRing(DateTimeOffset? last, DateTimeOffset now) => last is not { } previous || now - previous >= Gap;

    /// <summary>
    /// The chime as a WAV image. A <paramref name="peak"/> of 0 gives the same image with every sample silent, which shows that
    /// the audio device takes the format without making a sound.
    /// </summary>
    public static byte[] Render(double peak = Peak) => SoundRecipes.Render(SoundRecipes.Chime, peak);
}