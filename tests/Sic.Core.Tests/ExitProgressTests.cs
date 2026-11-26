using Sic.Core.Agents;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ExitProgressTests
{
    [Theory]
    [InlineData(1, 0, 0, "Waiting for the session to exit…")]
    [InlineData(3, 0, 0, "Waiting for 3 sessions to exit…")]
    [InlineData(3, 1, 0, "1 of 3 exited · waiting for the rest…")]
    [InlineData(3, 1, 1, "1 of 3 exited · waiting for the rest…")]
    [InlineData(3, 0, 1, "0 of 3 exited · waiting for the rest…")]
    [InlineData(1, 1, 0, "The session exited cleanly. Closing SicTerm…")]
    [InlineData(3, 3, 0, "All 3 sessions exited cleanly. Closing SicTerm…")]
    [InlineData(1, 0, 1, "The session did not exit in time and will be stopped. Closing SicTerm…")]
    [InlineData(3, 2, 1, "2 exited cleanly; 1 did not exit in time and will be stopped. Closing SicTerm…")]
    [InlineData(2, 0, 2, "2 did not exit in time and will be stopped. Closing SicTerm…")]
    public void TheSummarySaysWhereTheQuitHasGot(int total, int exited, int stubborn, string expected) =>
        Assert.Equal(expected, ExitProgress.Summary(total, exited, stubborn));

    [Fact]
    public void CountsBeyondTheTotalAreHeldToIt()
    {
        Assert.Equal("All 2 sessions exited cleanly. Closing SicTerm…", ExitProgress.Summary(2, 5, 0));
        Assert.Equal("1 exited cleanly; 1 did not exit in time and will be stopped. Closing SicTerm…", ExitProgress.Summary(2, 1, 4));
        Assert.Equal("Waiting for 2 sessions to exit…", ExitProgress.Summary(2, -1, -1));
        Assert.Equal("No sessions are running. Closing SicTerm…", ExitProgress.Summary(0, 0, 0));
    }

    [Fact]
    public void EachSessionShowsWhetherItIsStillBeingAskedExitedOrWillBeStopped()
    {
        Assert.Equal("Exiting…", ExitProgress.State(null));
        Assert.Equal("Exited", ExitProgress.State(true));
        Assert.Equal("Will be stopped", ExitProgress.State(false));
    }
}
