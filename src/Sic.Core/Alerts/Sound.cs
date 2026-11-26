namespace Sic.Core.Alerts;

/// <summary>
/// A completion sound SIC can ring (docs/decisions/completion-sounds.md): one built in, a recipe rendered in code
/// (<see cref="SoundRecipes"/>), or one of the user's own WAV files from the sounds folder (<see cref="SoundFiles"/>). The image
/// is the 16-bit PCM WAV that PlaySound takes from memory, made the first time it is wanted and kept.
/// </summary>
public sealed class Sound
{
    private readonly Lazy<byte[]> _image;

    public Sound(string id, string name, bool builtIn, bool cut, Func<byte[]> render)
    {
        Id = id; Name = name; BuiltIn = builtIn; Cut = cut;
        _image = new Lazy<byte[]>(render);
    }

    /// <summary>A recipe's word (chime, breath, tada), or a file's name with its extension (one-up.wav); compared without case.</summary>
    public string Id { get; }

    /// <summary>What the list shows: a recipe's name, or the file name.</summary>
    public string Name { get; }

    public bool BuiltIn { get; }

    /// <summary>True for a file longer than <see cref="SoundCatalog.MaxSeconds"/>, which plays its first that many seconds.</summary>
    public bool Cut { get; }

    /// <summary>The WAV image, 16-bit PCM.</summary>
    public byte[] Image => _image.Value;

    /// <summary>How long it plays, after any cut, read from the image.</summary>
    public double Seconds => WavImage.Seconds(Image);

    public static bool SameId(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The 16-bit PCM WAV image PlaySound takes from memory, written and read.</summary>
public static class WavImage
{
    public const int HeaderLength = 44;

    /// <summary>The image of 16-bit samples, interleaved when there are two channels.</summary>
    public static byte[] Write(short[] samples, int sampleRate, int channels)
    {
        var bytes = 2 * samples.Length;
        using var image = new MemoryStream(HeaderLength + bytes);
        using var writer = new BinaryWriter(image);
        writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVE"u8);
        // The format chunk: PCM, the channels, the rate, bytes per second, bytes per frame, bits per sample.
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(sampleRate);
        writer.Write(sampleRate * 2 * channels); writer.Write((short)(2 * channels)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(bytes);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return image.ToArray();
    }

    /// <summary>Samples in [-1, 1] scaled so the loudest reaches <paramref name="peak"/> of full scale, as 16-bit; a peak of 0 is the same length of silence.</summary>
    public static short[] Scale(double[] samples, double peak)
    {
        var loudest = 0.0;
        foreach (var sample in samples) loudest = Math.Max(loudest, Math.Abs(sample));
        var scale = loudest > 0 ? Math.Clamp(peak, 0, 1) * short.MaxValue / loudest : 0;
        var result = new short[samples.Length];
        for (var index = 0; index < result.Length; index++) result[index] = (short)Math.Round(samples[index] * scale);
        return result;
    }

    public static int Channels(byte[] image) => BitConverter.ToInt16(image, 22);

    public static int SampleRate(byte[] image) => BitConverter.ToInt32(image, 24);

    /// <summary>The image's length in seconds, from its header.</summary>
    public static double Seconds(byte[] image) => BitConverter.ToInt32(image, 40) / 2.0 / Channels(image) / SampleRate(image);

    /// <summary>The image's samples, interleaved, as 16-bit values.</summary>
    public static short[] Samples(byte[] image)
    {
        var samples = new short[BitConverter.ToInt32(image, 40) / 2];
        for (var index = 0; index < samples.Length; index++) samples[index] = BitConverter.ToInt16(image, HeaderLength + 2 * index);
        return samples;
    }
}
