using System.Text;
using Sic.Core.Alerts;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ReadyChimeTests
{
    private static readonly byte[] Image = ReadyChime.Render();

    private static short[] Samples(byte[] image)
    {
        var samples = new short[BitConverter.ToInt32(image, 40) / 2];
        for (var index = 0; index < samples.Length; index++) samples[index] = BitConverter.ToInt16(image, 44 + 2 * index);
        return samples;
    }

    /// <summary>The strength of one frequency over a stretch of the sound (the Goertzel filter), for telling the two notes apart.</summary>
    private static double Strength(short[] samples, double frequency, double fromSeconds, double toSeconds)
    {
        int from = (int)(fromSeconds * ReadyChime.SampleRate), to = (int)(toSeconds * ReadyChime.SampleRate);
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / ReadyChime.SampleRate);
        double previous = 0, beforePrevious = 0;
        for (var index = from; index < to; index++)
        {
            var current = samples[index] / 32768.0 + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }
        return Math.Sqrt(previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious) / (to - from);
    }

    [Fact]
    public void TheImageIsASixteenBitMonoWaveOfTheStatedLength()
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(Image, 0, 4));
        Assert.Equal(Image.Length - 8, BitConverter.ToInt32(Image, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(Image, 8, 8));
        Assert.Equal(16, BitConverter.ToInt32(Image, 16));
        Assert.Equal(1, BitConverter.ToInt16(Image, 20));
        Assert.Equal(1, BitConverter.ToInt16(Image, 22));
        Assert.Equal(ReadyChime.SampleRate, BitConverter.ToInt32(Image, 24));
        Assert.Equal(ReadyChime.SampleRate * 2, BitConverter.ToInt32(Image, 28));
        Assert.Equal(2, BitConverter.ToInt16(Image, 32));
        Assert.Equal(16, BitConverter.ToInt16(Image, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(Image, 36, 4));
        Assert.Equal(Image.Length - 44, BitConverter.ToInt32(Image, 40));
        Assert.Equal(ReadyChime.Length.TotalSeconds, Samples(Image).Length / (double)ReadyChime.SampleRate, 3);
    }

    [Fact]
    public void ItPeaksAtItsSoftLevelAndNeverClips()
    {
        var loudest = Samples(Image).Max(sample => Math.Abs((int)sample)) / 32767.0;
        Assert.Equal(ReadyChime.Peak, loudest, 2);
        Assert.True(ReadyChime.Peak < 0.5);
    }

    [Fact]
    public void ItStartsAndEndsInSilenceSoNeitherEdgeClicks()
    {
        var samples = Samples(Image);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);
        // Within the first millisecond the rise is still under a tenth of the peak, and so is the last millisecond of the fade.
        var tenth = ReadyChime.Peak * 32767 / 10;
        Assert.All(samples[..44], sample => Assert.True(Math.Abs((int)sample) < tenth));
        Assert.All(samples[^44..], sample => Assert.True(Math.Abs((int)sample) < tenth));
    }

    [Fact]
    public void TheFirstNoteIsE5AndTheSecondA5AFourthAbove()
    {
        var samples = Samples(Image);
        // Before the second note, E5 alone.
        Assert.True(Strength(samples, 659.255, 0.01, 0.11) > 20 * Strength(samples, 880, 0.01, 0.11));
        // Once it sounds, A5 leads while E5 dies away under it.
        Assert.True(Strength(samples, 880, 0.15, 0.40) > Strength(samples, 659.255, 0.15, 0.40));
        Assert.True(Strength(samples, 659.255, 0.15, 0.40) > 0);
    }

    [Fact]
    public void ItHasFadedToAWhisperWellBeforeItsEnd()
    {
        var samples = Samples(Image);
        var tail = samples[(int)(0.8 * ReadyChime.SampleRate)..].Max(sample => Math.Abs((int)sample)) / 32767.0;
        Assert.True(tail < ReadyChime.Peak / 5, $"tail {tail:0.000}");
    }

    [Fact]
    public void SilenceIsTheSameImageWithoutASound()
    {
        var silent = ReadyChime.Render(0);
        Assert.Equal(Image.Length, silent.Length);
        Assert.Equal(Image[..44], silent[..44]);
        Assert.All(Samples(silent), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void ChimesThatFallTogetherRingOnce()
    {
        var first = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        Assert.True(ReadyChime.MayRing(null, first));
        Assert.False(ReadyChime.MayRing(first, first + ReadyChime.Gap - TimeSpan.FromMilliseconds(1)));
        Assert.True(ReadyChime.MayRing(first, first + ReadyChime.Gap));
    }
}
