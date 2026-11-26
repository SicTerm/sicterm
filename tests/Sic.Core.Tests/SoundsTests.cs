using System.Text;
using Sic.Core.Alerts;
using Xunit;

namespace Sic.Core.Tests;

/// <summary>One collection: <see cref="SoundCatalog.Reload"/> changes the catalog every test reads.</summary>
[Collection("Sound catalog")]
public sealed class SoundsTests
{
    /// <summary>A WAV file's bytes: PCM (tag 1) or float (tag 3), as the extensible header when asked, holding a wave of the time.</summary>
    private static byte[] Wav(int tag, int channels, int rate, int bits, double seconds, Func<double, double> wave, bool extensible = false, int? declaredData = null)
    {
        var frames = (int)Math.Round(seconds * rate);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var formatLength = extensible ? 40 : 16;
        var dataLength = frames * channels * bits / 8;
        writer.Write("RIFF"u8); writer.Write(4 + 8 + formatLength + 8 + dataLength); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(formatLength);
        writer.Write((short)(extensible ? 0xFFFE : tag)); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * bits / 8); writer.Write((short)(channels * bits / 8)); writer.Write((short)bits);
        if (extensible)
        {
            writer.Write((short)22); writer.Write((short)bits); writer.Write(3);
            writer.Write((short)tag); writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
        }
        writer.Write("data"u8); writer.Write(declaredData ?? dataLength);
        for (var frame = 0; frame < frames; frame++)
        {
            var value = Math.Clamp(wave(frame / (double)rate), -1, 1);
            for (var channel = 0; channel < channels; channel++)
            {
                if (tag == 3) { if (bits == 32) writer.Write((float)value); else writer.Write(value); }
                else switch (bits)
                {
                    case 8: writer.Write((byte)Math.Round(value * 127 + 128)); break;
                    case 16: writer.Write((short)Math.Round(value * 32767)); break;
                    case 24: { var sample = (int)Math.Round(value * 8388607); writer.Write((byte)sample); writer.Write((byte)(sample >> 8)); writer.Write((byte)(sample >> 16)); break; }
                    default: writer.Write((int)Math.Round(value * 2147483647)); break;
                }
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static double Peak(byte[] image) => WavImage.Samples(image).Max(sample => Math.Abs((int)sample)) / 32767.0;

    [Fact]
    public void TheBuiltInSoundsAreTheChimeAndTwentyMore_EachRenderedUntilItHasRungOut()
    {
        Assert.Equal(21, SoundRecipes.All.Count);
        Assert.Equal("chime", SoundRecipes.All[0].Id);
        Assert.Equal(SoundRecipes.All.Count, SoundRecipes.All.Select(recipe => recipe.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var fadeSamples = (int)Math.Round(SoundRecipes.Fade * SoundRecipes.SampleRate);
        foreach (var recipe in SoundRecipes.All)
        {
            var image = SoundRecipes.Render(recipe);
            Assert.Equal(1, WavImage.Channels(image));
            Assert.Equal(SoundRecipes.SampleRate, WavImage.SampleRate(image));
            Assert.InRange(WavImage.Seconds(image), 0.1, SoundRecipes.Longest);
            var samples = WavImage.Samples(image);
            var loudest = samples.Max(sample => Math.Abs((int)sample));
            Assert.Equal(recipe.Peak, loudest / 32767.0, 2);
            // A soft start, a sound still above its rung-out floor (−40 dB) just before the fade, and nothing in the last sample.
            Assert.True(Math.Abs((int)samples[0]) < loudest / 10, recipe.Id + " starts loud");
            Assert.True(Math.Abs((int)samples[^(fadeSamples + 1)]) >= loudest * 0.0099 - 1, recipe.Id + " was cut before it rang out");
            Assert.All(samples[^fadeSamples..], sample => Assert.True(Math.Abs((int)sample) <= loudest * 0.0101 + 1, recipe.Id + " fades from above the floor"));
            Assert.Equal(0, samples[^1]);
        }
    }

    [Fact]
    public void TheChimeIsTheFirstRecipeAndRingsOut()
    {
        Assert.Equal(SoundRecipes.Render(SoundRecipes.Chime), ReadyChime.Render());
        Assert.InRange(ReadyChime.Length.TotalSeconds, 1.3, 1.7);
        Assert.Equal(ReadyChime.Length.TotalSeconds, SoundCatalog.Default.Seconds, 3);
        Assert.Equal("Chime", SoundCatalog.Default.Name);
    }

    [Theory]
    [InlineData(1, 1, 22050, 16, false)]
    [InlineData(1, 2, 44100, 8, false)]
    [InlineData(1, 1, 48000, 24, false)]
    [InlineData(1, 2, 8000, 32, false)]
    [InlineData(3, 1, 44100, 32, false)]
    [InlineData(3, 1, 96000, 64, false)]
    [InlineData(1, 2, 44100, 16, true)]
    public void AFileOfEachFormatIsBroughtToTheChimesLevelAtItsOwnRateAndChannels(int tag, int channels, int rate, int bits, bool extensible)
    {
        var bytes = Wav(tag, channels, rate, bits, 0.5, t => 0.9 * Math.Sin(2 * Math.PI * 440 * t), extensible);
        var image = SoundFiles.Convert(bytes, SoundCatalog.MaxSeconds, ReadyChime.Peak);
        Assert.Equal(channels, WavImage.Channels(image));
        Assert.Equal(rate, WavImage.SampleRate(image));
        Assert.Equal(0.5, WavImage.Seconds(image), 3);
        Assert.Equal(ReadyChime.Peak, Peak(image), 2);
        Assert.Equal(0.5, SoundFiles.Seconds(bytes), 3);
    }

    [Fact]
    public void AFileLongerThanTheLongestPlaysItsFirstSecondsAndFadesAtTheCut()
    {
        var bytes = Wav(1, 1, 8000, 16, 3.5, t => 0.5 * Math.Sin(2 * Math.PI * 300 * t));
        var image = SoundFiles.Convert(bytes, SoundCatalog.MaxSeconds, ReadyChime.Peak);
        Assert.Equal(SoundCatalog.MaxSeconds, WavImage.Seconds(image), 3);
        var samples = WavImage.Samples(image);
        Assert.Equal(0, samples[^1]);
        // Full strength until the fade: a fifth of a second before the end, still well above half the peak somewhere in that stretch.
        Assert.True(samples[^(8000 / 5)..^(8000 / 6)].Max(sample => Math.Abs((int)sample)) > ReadyChime.Peak * 32767 / 2);
        Assert.Equal(3.5, SoundFiles.Seconds(bytes), 3);
    }

    [Theory]
    [InlineData("hello", "not a WAV file")]
    [InlineData("4ch", "4 channels")]
    [InlineData("mp3", "format 85")]
    [InlineData("slow", "4000 Hz")]
    [InlineData("12bit", "12-bit PCM")]
    [InlineData("empty", "no samples")]
    [InlineData("nodata", "no data chunk")]
    public void AFileThatCannotBeUsedSaysWhy(string kind, string expected)
    {
        var bytes = kind switch
        {
            "hello" => Encoding.ASCII.GetBytes("hello, not a wav"),
            "4ch" => Wav(1, 4, 44100, 16, 0.1, _ => 0),
            "mp3" => Wav(85, 1, 44100, 16, 0.1, _ => 0),
            "slow" => Wav(1, 1, 4000, 16, 0.1, _ => 0),
            "12bit" => Wav(1, 1, 44100, 12, 0.1, _ => 0),
            "empty" => Wav(1, 1, 44100, 16, 0, _ => 0),
            _ => Wav(1, 1, 44100, 16, 0.1, _ => 0)[..36]
        };
        var exception = Assert.Throws<InvalidDataException>(() => SoundFiles.Convert(bytes, SoundCatalog.MaxSeconds, ReadyChime.Peak));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatSaysMoreDataThanItHoldsIsReadToItsEnd()
    {
        var bytes = Wav(1, 1, 8000, 16, 0.25, t => Math.Sin(2 * Math.PI * 200 * t), declaredData: 1_000_000);
        Assert.Equal(0.25, SoundFiles.Seconds(bytes), 3);
    }

    [Fact]
    public void ReadFolder_ListsGoodFilesInNameOrderAndReportsTheRest_AndTheCatalogPutsThemAfterTheBuiltInOnes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sic-sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "b-second.wav"), Wav(1, 1, 22050, 16, 0.2, t => Math.Sin(2 * Math.PI * 440 * t)));
            File.WriteAllBytes(Path.Combine(folder, "A-first.WAV"), Wav(1, 2, 44100, 8, 3.0, t => Math.Sin(2 * Math.PI * 220 * t)));
            File.WriteAllText(Path.Combine(folder, "broken.wav"), "nothing like a wave");
            File.WriteAllText(Path.Combine(folder, "note.txt"), "ignored");
            using (var big = new FileStream(Path.Combine(folder, "huge.wav"), FileMode.Create)) big.SetLength(SoundFiles.MaxBytes + 1);
            var (sounds, problems) = SoundFiles.ReadFolder(folder, SoundCatalog.MaxSeconds, ReadyChime.Peak);
            Assert.Equal(["A-first.WAV", "b-second.wav"], sounds.Select(sound => sound.Id));
            Assert.True(sounds[0].Cut); Assert.False(sounds[1].Cut);
            Assert.Equal(SoundCatalog.MaxSeconds, sounds[0].Seconds, 3);
            Assert.Equal(0.2, sounds[1].Seconds, 3);
            Assert.All(sounds, sound => Assert.False(sound.BuiltIn));
            Assert.Equal(["broken.wav", "huge.wav"], problems.Select(problem => problem.File));
            Assert.Contains("not a WAV file", problems[0].Error);
            Assert.Contains("MB", problems[1].Error);

            var reported = SoundCatalog.Reload(folder);
            Assert.Equal(2, reported.Count);
            Assert.Equal(SoundCatalog.BuiltIn.Count + 2, SoundCatalog.All.Count);
            Assert.Equal("A-first.WAV", SoundCatalog.All[SoundCatalog.BuiltIn.Count].Id);
            Assert.Same(SoundCatalog.All[SoundCatalog.BuiltIn.Count], SoundCatalog.Find("a-first.wav"));
            Assert.Same(SoundCatalog.Default, SoundCatalog.Find("gone.wav"));
            Assert.Same(SoundCatalog.Default, SoundCatalog.Find(null));
            Assert.True(SoundCatalog.IsBuiltIn("Chime")); Assert.False(SoundCatalog.IsBuiltIn("b-second.wav"));
            Assert.Equal(folder, SoundCatalog.Folder);
        }
        finally
        {
            SoundCatalog.Reload(null);
            Directory.Delete(folder, recursive: true);
        }
        Assert.Equal(SoundCatalog.BuiltIn.Count, SoundCatalog.All.Count);
        Assert.Empty(SoundCatalog.Problems);
        Assert.Equal("chime", SoundCatalog.Normalize("  "));
        Assert.Equal("tada", SoundCatalog.Normalize(" tada "));
    }

    [Fact]
    public void AFolderThatDoesNotExistHoldsNoSoundsAndIsNoProblem()
    {
        var (sounds, problems) = SoundFiles.ReadFolder(Path.Combine(Path.GetTempPath(), "sic-no-such-" + Guid.NewGuid().ToString("N")), SoundCatalog.MaxSeconds, ReadyChime.Peak);
        Assert.Empty(sounds); Assert.Empty(problems);
    }
}
