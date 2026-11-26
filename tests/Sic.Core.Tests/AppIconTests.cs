using Sic.Core.Appearance;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AppIconTests
{
    [Theory]
    [InlineData("orbit", "orbit")]
    [InlineData(" Mono-Disc ", "mono-disc")]
    [InlineData("shipped", "shipped")]
    [InlineData("motion-previous", "motion-previous")]
    [InlineData(null, "shipped")]
    [InlineData("", "shipped")]
    [InlineData("hurricane", "shipped")]
    // The shipped option's word up to 0.9.3: a settings file that saved the default then gets the icon that ships now.
    [InlineData("motion-fitted", "shipped")]
    public void AnUnknownWordGivesTheShippedIcon(string? id, string expected) => Assert.Equal(expected, AppIcon.Normalize(id));

    [Fact]
    public void EveryOptionIsDistinctAndNamesItsFiles()
    {
        Assert.Equal(AppIcon.All.Count, AppIcon.All.Select(option => option.Id).Distinct().Count());
        Assert.Equal(AppIcon.Default, AppIcon.All[0].Id);
        Assert.Equal("sic", AppIcon.Find(null).Icon);
        Assert.Equal("sic-light", AppIcon.IconFor(AppIcon.Find(null), lightTaskbar: true));
        foreach (var option in AppIcon.All)
        {
            Assert.Equal(option.Id, AppIcon.Normalize(option.Id));
            Assert.NotEmpty(option.Title);
            Assert.NotEmpty(option.Note);
            Assert.DoesNotContain('\\', option.Icon);
        }
    }

    [Fact]
    public void AOneInkOptionTakesItsLightFileOnALightTaskbarOnly()
    {
        var mono = AppIcon.Find("mono-line");
        Assert.Equal("options/mono-line-dark", AppIcon.IconFor(mono, lightTaskbar: false));
        Assert.Equal("options/mono-line", AppIcon.IconFor(mono, lightTaskbar: true));
        var hub = AppIcon.Find("lit-hub");
        Assert.Equal(AppIcon.IconFor(hub, lightTaskbar: false), AppIcon.IconFor(hub, lightTaskbar: true));
    }

    [Theory]
    // Without the accent on the taskbar, Windows' mode decides.
    [InlineData(true, false, null, true)]
    [InlineData(false, false, null, false)]
    [InlineData(true, false, unchecked((int)0xFF78691D), true)]
    // With it on, the accent's Start colour decides, in light mode too: the owner's #1D6978 (stored ABGR) is dark.
    [InlineData(true, true, unchecked((int)0xFF78691D), false)]
    [InlineData(false, true, unchecked((int)0xFF78691D), false)]
    // A light accent colour (#9EE9EB) takes the near-black icon even in dark mode.
    [InlineData(false, true, unchecked((int)0xFFEBE99E), true)]
    // The accent on with no colour stored: the mode decides.
    [InlineData(true, true, null, true)]
    public void TheTaskbarIsJudgedByTheColourWindowsPaintsIt(bool lightMode, bool accentOnTaskbar, int? startColorAbgr, bool expected) =>
        Assert.Equal(expected, AppIcon.LightTaskbar(lightMode, accentOnTaskbar, startColorAbgr));

    [Fact]
    public void TheDarkInkTakesOverWhereItHasTheMoreContrast()
    {
        // Contrast of white and of #111111 against a surface of luminance L, as WCAG computes it.
        static double White(double l) => 1.05 / (l + 0.05);
        static double Ink(double l) => (l + 0.05) / (AppIcon.RelativeLuminance(0x11, 0x11, 0x11) + 0.05);
        // At the threshold both read about 4.3:1 to 4.4:1 (the exact crossing is at 0.1916).
        Assert.True(Math.Abs(White(AppIcon.DarkInkAbove) - Ink(AppIcon.DarkInkAbove)) < 0.1);
        Assert.False(AppIcon.PrefersDarkInk(AppIcon.RelativeLuminance(0x20, 0x20, 0x20)));
        Assert.True(AppIcon.PrefersDarkInk(AppIcon.RelativeLuminance(0xF3, 0xF3, 0xF3)));
        // SIC light's rail, a dark teal, takes the white mark.
        Assert.False(AppIcon.PrefersDarkInk(AppIcon.RelativeLuminance(0x00, 0x40, 0x50)));
    }

    [Fact]
    public void APreviewTakesTheExactFrameOrTheNextLargerOne()
    {
        Assert.Equal(24, AppIcon.PreviewSize(24, _ => true));
        Assert.Equal(28, AppIcon.PreviewSize(27, _ => true));
        Assert.Equal(30, AppIcon.PreviewSize(29, _ => true));
        Assert.Equal(42, AppIcon.PreviewSize(48, size => size != 48));
        Assert.Equal(16, AppIcon.PreviewSize(12, _ => true));
    }

    [Fact]
    public void Save_KeepsTheAppIconAndReadsAnOlderFileWithTheShippedOne()
    {
        var path = Path.Combine(Path.GetTempPath(), "sic-app-icon-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new JsonStateStore<AppPreferences>(path).Save(new AppPreferences { AppIcon = "orbit" });
            Assert.Equal("orbit", new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences")).AppIcon);
            File.WriteAllText(path, "{\"Version\":1,\"Theme\":\"sic-dark\"}");
            Assert.Equal(AppIcon.Default, new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences")).AppIcon);
        }
        finally { File.Delete(path); }
    }
}
