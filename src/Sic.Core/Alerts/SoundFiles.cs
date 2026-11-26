using System.Buffers.Binary;

namespace Sic.Core.Alerts;

/// <summary>A sound file that could not be used, and why: shown in Settings and logged, never played.</summary>
public sealed record SoundProblem(string File, string Error);

/// <summary>
/// The user's own sounds: WAV files in the sounds folder under SIC's data folder (docs/decisions/completion-sounds.md). A file is
/// PCM or IEEE float, mono or stereo, 8 to 192 kHz, up to <see cref="MaxBytes"/>; it is read and checked when the folder is read,
/// and decoded when first played: cut to the catalog's longest length with a short fade when it is longer, its peak brought to the
/// chime's level so no file blasts beside the built-in sounds, and written as 16-bit PCM at its own rate and channels.
/// </summary>
public static class SoundFiles
{
    public const string Extension = ".wav";
    public const long MaxBytes = 5 * 1024 * 1024;
    /// <summary>The fade at the end of a cut, so the cut does not click.</summary>
    public const double CutFade = 0.15;

    private sealed record Format(int Tag, int Channels, int SampleRate, int Bits, int DataOffset, int DataLength)
    {
        public int FrameBytes => Channels * Bits / 8;
        public int Frames => DataLength / FrameBytes;
    }

    /// <summary>
    /// Every sound in a folder's *.wav files, in file-name order, and the files that could not be used with why. A folder that
    /// does not exist holds no sounds and is no problem.
    /// </summary>
    public static (IReadOnlyList<Sound> Sounds, IReadOnlyList<SoundProblem> Problems) ReadFolder(string folder, double maxSeconds, double peak)
    {
        var sounds = new List<Sound>();
        var problems = new List<SoundProblem>();
        if (!Directory.Exists(folder)) return (sounds, problems);
        foreach (var path in Directory.EnumerateFiles(folder, "*" + Extension).Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.GetFileName(path);
            if (!file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;
            try { sounds.Add(Read(path, maxSeconds, peak)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { problems.Add(new(file, "could not be read: " + exception.Message)); }
            catch (InvalidDataException exception) { problems.Add(new(file, exception.Message)); }
        }
        return (sounds, problems);
    }

    /// <summary>Reads one file: checked now, decoded when first played. Throws <see cref="InvalidDataException"/> saying why for a file that cannot be used.</summary>
    public static Sound Read(string path, double maxSeconds, double peak)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxBytes) throw new InvalidDataException($"{length / 1048576.0:0.0} MB; up to {MaxBytes / 1048576} MB is read");
        var bytes = File.ReadAllBytes(path);
        var format = Parse(bytes);
        var cut = format.Frames > maxSeconds * format.SampleRate;
        var name = Path.GetFileName(path);
        return new Sound(name, name, builtIn: false, cut, () => Convert(bytes, format, maxSeconds, peak));
    }

    /// <summary>A WAV file's bytes as the image a sound plays: for tests and the smoke, the same conversion a read file gets.</summary>
    public static byte[] Convert(byte[] bytes, double maxSeconds, double peak) => Convert(bytes, Parse(bytes), maxSeconds, peak);

    /// <summary>How long a file holds, in seconds, before any cut.</summary>
    public static double Seconds(byte[] bytes) { var format = Parse(bytes); return format.Frames / (double)format.SampleRate; }

    private static Format Parse(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("not a WAV file (no RIFF WAVE header)");
        int tag = 0, channels = 0, rate = 0, bits = 0, dataOffset = -1, dataLength = 0;
        var formatSeen = false;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = bytes.AsSpan(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (size < 0) throw new InvalidDataException("a chunk with a negative size");
            var body = offset + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > bytes.Length) throw new InvalidDataException("a format chunk too short to read");
                tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14, 2));
                // WAVE_FORMAT_EXTENSIBLE: the real tag is the first two bytes of the sub-format GUID.
                if (tag == 0xFFFE)
                {
                    if (size < 40 || body + 26 > bytes.Length) throw new InvalidDataException("an extensible format chunk too short to read");
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 24, 2));
                }
                formatSeen = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                dataOffset = body;
                // A file still being written, or cut short, says more data than it holds; what is there is read.
                dataLength = (int)Math.Min(size, bytes.Length - body);
            }
            offset = body + size + (size & 1);
        }
        if (!formatSeen) throw new InvalidDataException("no format chunk");
        if (dataOffset < 0) throw new InvalidDataException("no data chunk");
        if (tag is not (1 or 3)) throw new InvalidDataException($"format {tag}; PCM (1) and IEEE float (3) are read, not compressed formats");
        if (channels is not (1 or 2)) throw new InvalidDataException($"{channels} channels; mono or stereo is read");
        if (rate is < 8000 or > 192_000) throw new InvalidDataException($"{rate} Hz; 8 to 192 kHz is read");
        if (tag == 1 && bits is not (8 or 16 or 24 or 32)) throw new InvalidDataException($"{bits}-bit PCM; 8, 16, 24 and 32 are read");
        if (tag == 3 && bits is not (32 or 64)) throw new InvalidDataException($"{bits}-bit float; 32 and 64 are read");
        var format = new Format(tag, channels, rate, bits, dataOffset, dataLength);
        if (format.Frames == 0) throw new InvalidDataException("no samples");
        return format;
    }

    private static byte[] Convert(byte[] bytes, Format format, double maxSeconds, double peak)
    {
        var frames = Math.Min(format.Frames, (int)Math.Floor(maxSeconds * format.SampleRate));
        var samples = new double[frames * format.Channels];
        var bytesPerSample = format.Bits / 8;
        for (var index = 0; index < samples.Length; index++)
        {
            var span = bytes.AsSpan(format.DataOffset + index * bytesPerSample, bytesPerSample);
            samples[index] = format.Tag == 3
                ? format.Bits == 32 ? BinaryPrimitives.ReadSingleLittleEndian(span) : BinaryPrimitives.ReadDoubleLittleEndian(span)
                : format.Bits switch
                {
                    8 => (span[0] - 128) / 128.0,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(span) / 32768.0,
                    24 => ((span[0] | span[1] << 8 | span[2] << 16) << 8 >> 8) / 8388608.0,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(span) / 2147483648.0
                };
        }
        if (frames < format.Frames)
        {
            // Cut: the last CutFade seconds fade to nothing.
            var fadeFrames = Math.Min(frames, (int)Math.Round(CutFade * format.SampleRate));
            var from = frames - fadeFrames;
            for (var frame = from; frame < frames; frame++)
            {
                var gain = 0.5 + 0.5 * Math.Cos(Math.PI * (frame - from) / Math.Max(1, fadeFrames - 1));
                for (var channel = 0; channel < format.Channels; channel++) samples[frame * format.Channels + channel] *= gain;
            }
        }
        return WavImage.Write(WavImage.Scale(samples, peak), format.SampleRate, format.Channels);
    }
}
