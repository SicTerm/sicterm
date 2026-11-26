using Sic.Core.Appearance;
using Sic.Core.Limits;
using Xunit;

namespace Sic.Core.Tests;

public sealed class UsageLimitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 14, 0, 0, TimeSpan.Zero);

    /// <summary>A five-hour window <paramref name="elapsed"/> percent through, with <paramref name="used"/> percent used.</summary>
    private static LimitWindow FiveHour(double used, double elapsed) =>
        new(used, Now + UsageLimits.FiveHours * (1 - elapsed / 100), UsageLimits.FiveHours);

    [Theory]
    [InlineData(31, 48, LimitState.OnPace)]
    [InlineData(40, 35, LimitState.OnPace)]
    [InlineData(45, 35, LimitState.OnPace)]
    [InlineData(45.5, 35, LimitState.Ahead)]
    [InlineData(8, 1, LimitState.OnPace)]
    [InlineData(58, 35, LimitState.Ahead)]
    [InlineData(89.9, 10, LimitState.Ahead)]
    [InlineData(90, 99, LimitState.Near)]
    [InlineData(99.9, 50, LimitState.Near)]
    [InlineData(100, 82, LimitState.Reached)]
    public void TheStateFollowsThePaceWithTenPointsOfToleranceAndTurnsNearAtNinety(double used, double elapsed, LimitState expected) =>
        Assert.Equal(expected, UsageLimits.State(FiveHour(used, elapsed), Now));

    [Fact]
    public void AWindowThatHasResetOrIsMissingIsUnknown()
    {
        Assert.Equal(LimitState.Unknown, UsageLimits.State(null, Now));
        Assert.Equal(LimitState.Unknown, UsageLimits.State(new LimitWindow(40, Now.AddSeconds(-1), UsageLimits.FiveHours), Now));
    }

    [Fact]
    public void ElapsedTimeIsTheShareOfTheWindowPassedAndTheLabelNamesTheLength()
    {
        Assert.Equal(48, FiveHour(0, 48).ElapsedPercent(Now), 6);
        Assert.Equal(0, new LimitWindow(0, Now.AddDays(9), UsageLimits.SevenDays).ElapsedPercent(Now));
        Assert.Equal("5h", FiveHour(0, 0).Label);
        Assert.Equal("7d", new LimitWindow(0, Now, UsageLimits.SevenDays).Label);
    }

    [Fact]
    public void ClaudesStatusLineGivesBothWindowsAndNothingBeforeTheFirstReply()
    {
        const string json = """
            {"session_id":"x","rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1791900000},"seven_day":{"used_percentage":41.2,"resets_at":1792300000}}}
            """;
        var reading = UsageLimits.FromClaudeStatusLine(json, Now)!;
        Assert.Equal(new LimitWindow(23.5, DateTimeOffset.FromUnixTimeSeconds(1791900000), UsageLimits.FiveHours), reading.Short);
        Assert.Equal(new LimitWindow(41.2, DateTimeOffset.FromUnixTimeSeconds(1792300000), UsageLimits.SevenDays), reading.Long);
        Assert.Equal(Now, reading.ReadAt);
        Assert.Null(UsageLimits.FromClaudeStatusLine("""{"session_id":"x","model":{"id":"m"}}""", Now));
        Assert.Null(UsageLimits.FromClaudeStatusLine("""{"rate_limits":{"spend_limit":{"used_percentage":5,"resets_at":1}}}""", Now));
        Assert.Null(UsageLimits.FromClaudeStatusLine("not json", Now));
        var weekOnly = UsageLimits.FromClaudeStatusLine("""{"rate_limits":{"seven_day":{"used_percentage":3,"resets_at":1792300000}}}""", Now)!;
        Assert.Null(weekOnly.Short);
        Assert.Equal(3, weekOnly.Long!.UsedPercent);
    }

    [Fact]
    public void ACodexRecordSortsItsWindowsByLength()
    {
        // As written by codex-cli 0.162 on a plan with a weekly window alone.
        const string weekly = """{"timestamp":"2026-10-06T19:01:54.110Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","primary":{"used_percent":8.0,"window_minutes":10080,"resets_at":1791630016},"secondary":null,"plan_type":"prolite"}}}""";
        var reading = UsageLimits.FromCodexRecord(weekly)!;
        Assert.Null(reading.Short);
        Assert.Equal(new LimitWindow(8, DateTimeOffset.FromUnixTimeSeconds(1791630016), UsageLimits.SevenDays), reading.Long);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T19:01:54.110Z"), reading.ReadAt);
        const string both = """{"timestamp":"2026-10-06T19:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12,"window_minutes":300,"resets_in_seconds":3600},"secondary":{"used_percent":30,"window_minutes":10080,"resets_in_seconds":86400}}}}""";
        reading = UsageLimits.FromCodexRecord(both)!;
        Assert.Equal(new LimitWindow(12, DateTimeOffset.Parse("2026-10-06T20:00:00Z"), UsageLimits.FiveHours), reading.Short);
        Assert.Equal(30, reading.Long!.UsedPercent);
        Assert.Null(UsageLimits.FromCodexRecord("""{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"other","primary":{"used_percent":1,"window_minutes":300,"resets_at":1}}}}"""));
        Assert.Null(UsageLimits.FromCodexRecord("""{"type":"response_item","payload":{"type":"message"}}"""));
    }

    [Fact]
    public void ACodexLogGivesItsLastReadingFromItsTail()
    {
        var path = Path.Combine(Path.GetTempPath(), "sic-limits-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var filler = """{"type":"response_item","payload":{"type":"message","content":"__"}}""".Replace("__", new string('x', 2000));
            File.WriteAllLines(path, [
                """{"timestamp":"2026-10-06T18:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":10080,"resets_at":1791630016}}}}""",
                .. Enumerable.Repeat(filler, 50),
                """{"timestamp":"2026-10-06T19:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":9,"window_minutes":10080,"resets_at":1791630016}}}}""",
                filler, """{"timestamp":"2026-10-06T19:05:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":null}}"""]);
            Assert.Equal(9, UsageLimits.FromCodexRollout(path)!.Long!.UsedPercent);
            Assert.Null(UsageLimits.FromCodexRollout(path, tailBytes: 1000));
            Assert.Null(UsageLimits.FromCodexRollout(path + ".missing"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheDayFolderIsNamedByTheLocalDateAsCodexNamesIt()
    {
        Assert.Equal(Path.Combine(@"C:\Users\me\.codex", "sessions", "2026", "10", "06"), UsageLimits.CodexDayFolder(@"C:\Users\me\.codex", new DateTime(2026, 10, 6, 23, 59, 0)));
        Assert.Equal(Path.Combine(@"C:\Users\me\.codex", "sessions", "2027", "01", "01"), UsageLimits.CodexDayFolder(@"C:\Users\me\.codex", new DateTime(2027, 1, 1)));
    }

    [Fact]
    public void AKeptReadingRoundTripsAndABrokenOneIsNone()
    {
        var reading = new LimitReading(FiveHour(31, 48), null, Now);
        Assert.Equal(reading, UsageLimits.Deserialize(UsageLimits.Serialize(reading)));
        Assert.Null(UsageLimits.Deserialize("{\"Short\":"));
        Assert.Null(UsageLimits.Deserialize(""));
        Assert.Same(reading, UsageLimits.Latest(reading, reading with { ReadAt = Now.AddMinutes(-1) }));
    }

    public static TheoryData<string> AllThemes()
    {
        var data = new TheoryData<string>();
        foreach (var theme in Themes()) data.Add(theme.Id);
        return data;
    }

    /// <summary>Every built-in theme, the nineteen popular palettes among them.</summary>
    private static IEnumerable<ThemeDefinition> Themes() => ThemeCatalog.BuiltIn;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperIntelligentChat.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found.");
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void EveryThemesMetersReachThreeToOneAndItsTextFourAndAHalf(string id)
    {
        var palette = LimitColors.For(Themes().Single(theme => theme.Id == id));
        foreach (var meter in new[] { palette.OnPace, palette.Ahead, palette.Near })
            Assert.True(LimitColors.Contrast(meter, palette.Surface) >= LimitColors.MeterContrast - 0.01, $"{id}: {meter} on {palette.Surface}");
        Assert.True(LimitColors.Contrast(palette.Ink, palette.Surface) >= LimitColors.TextContrast - 0.01, $"{id}: ink {palette.Ink} on {palette.Surface}");
    }

    [Fact]
    public void TheThemesOwnHuesAreKeptWhenTheyAreDistinct()
    {
        var sicDark = LimitColors.For(ThemeCatalog.BuiltIn.Single(theme => theme.Id == "sic-dark"));
        Assert.False(sicDark.Standard);
        Assert.True(sicDark.Dark);
        // The success green, made vivid: the hue stays green.
        var (hue, _, _) = StateDots.ToHsl(Convert.ToByte(sicDark.OnPace[..2], 16), Convert.ToByte(sicDark.OnPace[2..4], 16), Convert.ToByte(sicDark.OnPace[4..], 16));
        Assert.InRange(hue, 120, 150);
    }

    [Theory]
    [InlineData("2E9E4F", "E0A526", "E5342B", true)]
    [InlineData("31748F", "F6C177", "EB6F92", true)]
    [InlineData("2E9E4F", "E5342B", "E5342B", false)]
    [InlineData("2E9E4F", "E0A526", "E8B030", false)]
    [InlineData("808080", "E0A526", "E5342B", false)]
    [InlineData("FF0010", "E0A526", "FF1000", false)]
    public void StatesTooCloseInHueOrGreyAreNotDistinct(string success, string warning, string danger, bool distinct) =>
        Assert.Equal(distinct, LimitColors.Distinct([success, warning, danger]));

    [Fact]
    public void AnAwkwardThemeFallsBackToStandardColoursAndStillReachesTheFloor()
    {
        var baseTheme = ThemeCatalog.BuiltIn.Single(theme => theme.Id == "sic-light");
        foreach (var theme in new[]
        {
            baseTheme with { Success = "777777", Warning = "888888", Danger = "999999" },
            baseTheme with { Warning = "B02519", Danger = "B02519" },
            baseTheme with { Rail = "FAFAFA", Base = "FFFFFF", RailText = "DDDDDD", Success = "CCFFCC", Warning = "FFF4CC", Danger = "FFDDDD" },
            baseTheme with { Rail = "000000", Base = "000000", RailText = "222222", Success = "003300", Warning = "332200", Danger = "330000" },
        })
        {
            var palette = LimitColors.For(theme);
            foreach (var meter in new[] { palette.OnPace, palette.Ahead, palette.Near })
                Assert.True(LimitColors.Contrast(meter, palette.Surface) >= LimitColors.MeterContrast - 0.01, $"{meter} on {palette.Surface}");
            Assert.True(LimitColors.Contrast(palette.Ink, palette.Surface) >= LimitColors.TextContrast - 0.01, $"ink {palette.Ink} on {palette.Surface}");
        }
        Assert.True(LimitColors.For(baseTheme with { Warning = "B02519", Danger = "B02519" }).Standard);
    }
}
