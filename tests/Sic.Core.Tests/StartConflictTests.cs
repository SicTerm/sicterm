using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class StartConflictTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);
    private const string Session = "941efcc1-557b-4f8f-947a-b0fdb607d31c";

    [Fact]
    public void ARequestOvertakenWhileItRefreshedNamesTheLaterOneThatRuns()
    {
        var conflict = new StartConflict
        {
            Request = new("a double-click on the conversation in Sessions", Now.AddSeconds(-3.4)),
            Holder = new("the pane's Resume conversation button", Now.AddSeconds(-2.8)),
            Running = true, Agent = "Claude Code", Tab = "drag fixes", SessionId = Session,
            ProcessId = 12345, ProcessStartedAt = Now.AddSeconds(-1.2), Refresh = TimeSpan.FromSeconds(3.1), Checks = TimeSpan.FromSeconds(0.2), Now = Now
        };
        Assert.Equal("This pane is already running, so this request to start it was skipped and the running terminal left as it is. "
            + "Claude Code in tab “drag fixes”, session " + Session + ". "
            + "This request came from a double-click on the conversation in Sessions 3.4 s ago; its history refresh took 3.1 s and its saved-history checks 0.2 s. "
            + "The running terminal was asked for 0.6 s later, from the pane's Resume conversation button, and has run as process 12345 for 1.2 s.",
            conflict.Describe());
        Assert.Equal("This pane is already running; the second start was skipped.", conflict.Summary);
    }

    [Fact]
    public void ASecondRequestWhileTheFirstIsStillStartingSaysSo()
    {
        var conflict = new StartConflict
        {
            Request = new("the Resume button in Sessions", Now.AddSeconds(-0.9)),
            Holder = new("a double-click on the conversation in Sessions", Now.AddSeconds(-1.2)),
            Running = false, Agent = "Hermes", Tab = "nightly", PaneNumber = 2, PaneCount = 3, SessionId = "20260928_101500_ab12",
            Refresh = TimeSpan.FromSeconds(0.4), Checks = TimeSpan.FromSeconds(0.1), Now = Now
        };
        Assert.Equal("This pane is still starting, so this request to start it was skipped and the starting terminal left as it is. "
            + "Hermes in tab “nightly”, pane 2 of 3, session 20260928_101500_ab12. "
            + "This request came from the Resume button in Sessions 0.9 s ago; its history refresh took 0.4 s and its saved-history checks 0.1 s. "
            + "The starting terminal was asked for 0.3 s earlier, from a double-click on the conversation in Sessions, and its terminal has not finished loading.",
            conflict.Describe());
        Assert.Equal("This pane is still starting; the second start was skipped.", conflict.Summary);
    }

    [Fact]
    public void AShellWithoutSessionOrRefreshInAClosedPaneLeavesThoseOut()
    {
        var conflict = new StartConflict
        {
            Request = new("the pane's Start terminal button", Now.AddSeconds(-0.2)), Holder = new("the pane's Start terminal button", Now.AddSeconds(-0.22)),
            Running = true, Agent = "PowerShell", ProcessId = 7, Now = Now
        };
        Assert.Equal("This pane is already running, so this request to start it was skipped and the running terminal left as it is. "
            + "PowerShell in a pane that has since closed. This request came from the pane's Start terminal button 0.2 s ago. "
            + "The running terminal was asked for at the same moment, from the pane's Start terminal button, and runs as process 7.",
            conflict.Describe());
    }

    [Theory]
    [InlineData(-3, "0.0 s")]
    [InlineData(0.04, "0.0 s")]
    [InlineData(9.94, "9.9 s")]
    [InlineData(42.4, "42 s")]
    [InlineData(119, "119 s")]
    [InlineData(185, "3 min")]
    [InlineData(9000, "2.5 h")]
    public void ElapsedTimeReadsInTheLargestSensibleUnit(double seconds, string expected) => Assert.Equal(expected, StartConflict.Elapsed(TimeSpan.FromSeconds(seconds)));
}
