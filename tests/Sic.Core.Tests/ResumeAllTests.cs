using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ResumeAllTests
{
    private static IReadOnlyList<PaneStartState> Tab(params PaneStartState[] panes) => panes;

    /// <summary>The steps as "tab:pane,pane", since a step's pane list compares by reference.</summary>
    private static IEnumerable<string> Steps(ResumeAllPlan plan) => plan.Steps.Select(step => step.Tab + ":" + string.Join(",", step.Panes));

    [Fact]
    public void TheSelectedTabGoesFirstAndOnlyResumablePanesAreStarted()
    {
        var plan = ResumeAll.Plan(
        [
            Tab(PaneStartState.Resumable),
            Tab(PaneStartState.Running, PaneStartState.Resumable, PaneStartState.Closing, PaneStartState.Resumable),
            Tab(PaneStartState.Unlinked),
            Tab(PaneStartState.Running)
        ], selectedTab: 1, live: 1);
        Assert.Equal(["1:1,3", "0:0"], Steps(plan));
        Assert.Equal(3, plan.Requested);
        Assert.Equal(2, plan.Running);
        Assert.Equal(1, plan.Unlinked);
        Assert.Equal(0, plan.LeftOver);
    }

    [Fact]
    public void APlaceholderOfAnAgentThatIsOffIsLeftAloneTakesNoRoomAndIsNamed()
    {
        var plan = ResumeAll.Plan([Tab(PaneStartState.AgentOff, PaneStartState.Resumable), Tab(PaneStartState.AgentOff)], selectedTab: 0, live: 10, limit: 11);
        Assert.Equal(["0:1"], Steps(plan));
        Assert.Equal(2, plan.AgentOff);
        Assert.Equal(0, plan.LeftOver);
        var (title, message, trouble) = ResumeAll.Summarize(new(plan.Requested, 1, [], plan.Running, plan.Unlinked, plan.LeftOver, false, plan.AgentOff));
        Assert.Equal("Resumed 1 session", title);
        Assert.Equal("2 not started: their agents are off in Settings → Agents.", message);
        Assert.False(trouble);
        var none = ResumeAll.Summarize(new(0, 0, [], 0, 0, 0, false, 1));
        Assert.Equal("Nothing to resume", none.Title);
        Assert.Equal("1 not started: its agent is off in Settings → Agents.", none.Message);
    }

    [Fact]
    public void WithNoSelectedTabTheStripOrderStands()
    {
        var plan = ResumeAll.Plan([Tab(PaneStartState.Resumable), Tab(PaneStartState.Resumable)], selectedTab: -1, live: 0);
        Assert.Equal([0, 1], plan.Steps.Select(step => step.Tab));
    }

    [Fact]
    public void TheLiveLimitCapsTheRunAndCountsWhatIsLeft()
    {
        var tabs = new[] { Tab(PaneStartState.Resumable, PaneStartState.Resumable), Tab(PaneStartState.Resumable), Tab(PaneStartState.Resumable, PaneStartState.Resumable) };
        var plan = ResumeAll.Plan(tabs, selectedTab: 2, live: ResumeAll.LiveLimit - 2);
        // Two seats left of the limit: both go to the selected tab, and the three others are left.
        Assert.Equal(["2:0,1"], Steps(plan));
        Assert.Equal(3, plan.LeftOver);
        var full = ResumeAll.Plan(tabs, selectedTab: 0, live: ResumeAll.LiveLimit);
        Assert.Empty(full.Steps);
        Assert.Equal(5, full.LeftOver);
        Assert.Empty(ResumeAll.Plan(tabs, selectedTab: 0, live: ResumeAll.LiveLimit + 8).Steps);
    }

    [Fact]
    public void AnEmptyStripPlansNothing() => Assert.Empty(ResumeAll.Plan([], selectedTab: -1, live: 0).Steps);

    [Fact]
    public void AFullRunIsItsTitleAlone()
    {
        Assert.Equal(("Resumed 1 session", "", false), ResumeAll.Summarize(new(1, 1, [], 0, 0, 0, false)));
        // Panes already running go unsaid, since the tabs show them; one left unlinked does not show, so it is said.
        Assert.Equal(("Resumed 3 sessions", "", false), ResumeAll.Summarize(new(3, 3, [], 2, 0, 0, false)));
        Assert.Equal(("Resumed 3 sessions", "1 not linked to a saved conversation.", false), ResumeAll.Summarize(new(3, 3, [], 2, 1, 0, false)));
    }

    [Fact]
    public void FailuresAndTheLimitMakeTheNoticeAWarningThatNamesEach()
    {
        var failure = @"PowerShell in “nightly” did not start: The folder this terminal ran in no longer exists: D:\nightly.";
        var (title, message, trouble) = ResumeAll.Summarize(new(4, 3, [failure], 0, 0, 2, false));
        Assert.True(trouble);
        Assert.Equal("Resumed 3 of 6 sessions", title);
        Assert.Equal(failure + "\n2 not started: the limit is 32 panes running at once, across all tabs.", message);
        Assert.Equal("1 not started: the limit is 4 panes running at once, across all tabs.", ResumeAll.Summarize(new(3, 3, [], 0, 0, 1, false), limit: 4).Message);
    }

    [Fact]
    public void NothingToResumeAndAQuitAreSaidPlainly()
    {
        Assert.Equal(("Nothing to resume", "", false), ResumeAll.Summarize(new(0, 0, [], 0, 0, 0, false)));
        Assert.Equal(("Nothing to resume", "All sessions are already running.", false), ResumeAll.Summarize(new(0, 0, [], 3, 0, 0, false)));
        Assert.Equal(("Nothing to resume", "1 not linked to a saved conversation.", false), ResumeAll.Summarize(new(0, 0, [], 3, 1, 0, false)));
        var stopped = ResumeAll.Summarize(new(5, 2, [], 0, 0, 0, true));
        Assert.Equal("Resume all stopped", stopped.Title);
        Assert.Equal("SicTerm is quitting; the rest were not started.", stopped.Message);
        Assert.True(stopped.Trouble);
    }
}
