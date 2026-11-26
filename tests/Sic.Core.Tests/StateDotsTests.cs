using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class StateDotsTests
{
    private static (byte R, byte G, byte B) Rgb(string hex) => (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));

    // The built-in themes' success and warning on their strips: SIC light's strip is dark (its rail) under light panels.
    [Theory]
    [InlineData("2E9E4F", true)] [InlineData("E0A526", true)]   // SIC dark
    [InlineData("1E7D3C", true)] [InlineData("B9851B", true)]   // SIC light, on its dark rail
    [InlineData("6CCB5F", true)] [InlineData("FCE100", true)]   // Windows dark
    [InlineData("0F7B0F", false)] [InlineData("9D5D00", false)] // Windows light
    [InlineData("82DDB4", true)] [InlineData("EDCB79", true)]   // Harbor dark
    [InlineData("187446", false)] [InlineData("8C5900", false)] // Harbor light
    public void ADotKeepsItsHueAndIsVividForItsSurface(string hex, bool onDark)
    {
        var (r, g, b) = Rgb(hex);
        var (hue, _, _) = StateDots.ToHsl(r, g, b);
        var (vr, vg, vb) = StateDots.Vivid(r, g, b, onDark);
        var (vividHue, saturation, lightness) = StateDots.ToHsl(vr, vg, vb);
        Assert.InRange(Math.Abs(vividHue - hue), 0, 1.5);
        Assert.InRange(saturation, StateDots.Saturation - 0.02, 1);
        if (onDark) Assert.InRange(lightness, StateDots.LightnessOnDark - 0.01, 1);
        else Assert.InRange(lightness, 0, StateDots.LightnessOnLight + 0.01);
    }

    [Fact]
    public void ADarkGreenOnADarkStripIsLifted()
    {
        var (r, g, b) = StateDots.Vivid(0x1E, 0x7D, 0x3C, onDark: true);
        var (_, _, before) = StateDots.ToHsl(0x1E, 0x7D, 0x3C);
        var (_, _, after) = StateDots.ToHsl(r, g, b);
        Assert.True(after > before + 0.2, $"lightness {before:F2} -> {after:F2}");
    }

    [Fact]
    public void AVividColorIsLeftAsItIs() => Assert.Equal(((byte)0x50, (byte)0xFA, (byte)0x7B), StateDots.Vivid(0x50, 0xFA, 0x7B, onDark: true));

    [Fact]
    public void AGreyHasNoHueToKeepAndIsLeftAsItIs() => Assert.Equal(((byte)0x80, (byte)0x80, (byte)0x80), StateDots.Vivid(0x80, 0x80, 0x80, onDark: true));

    [Theory]
    [InlineData("2E9E4F")] [InlineData("E0A526")] [InlineData("FF0000")] [InlineData("00FF00")] [InlineData("0000FF")] [InlineData("123456")] [InlineData("FEFEFE")]
    public void HslRoundTripsWithinAStep(string hex)
    {
        var (r, g, b) = Rgb(hex);
        var (hue, saturation, lightness) = StateDots.ToHsl(r, g, b);
        var (r2, g2, b2) = StateDots.FromHsl(hue, saturation, lightness);
        Assert.InRange(Math.Abs(r - r2), 0, 1); Assert.InRange(Math.Abs(g - g2), 0, 1); Assert.InRange(Math.Abs(b - b2), 0, 1);
    }

    [Fact]
    public void TheBlinkIsTheCursorsCadenceAndDimsRatherThanHides()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(600), StateDots.BlinkHalf);
        Assert.InRange(StateDots.BlinkDim, 0.2, 0.5);
    }
}
