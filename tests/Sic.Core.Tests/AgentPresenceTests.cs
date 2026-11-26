using Sic.Core.Agents;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AgentPresenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    // Every title the three agents set in the recordings of September 28, 2026, and what it says.
    [Theory]
    [InlineData("claude", "claude", null)]
    [InlineData("claude", "✳ Claude Code", false)]
    [InlineData("claude", "◐ Claude Code", true)]
    [InlineData("claude", "◑ Claude Code", true)]
    [InlineData("claude", "◑ Reply with ok", true)]
    [InlineData("claude", "✳ Reply with ok", false)]
    [InlineData("claude", "", null)]
    [InlineData("claude", @"C:\Program Files\PowerShell\7\pwsh.exe", null)]
    [InlineData("codex", "probe-folder", null)]
    [InlineData("codex", "⠋ | probe-folder", true)]
    [InlineData("codex", "Reply with ok | probe-folder", null)]
    [InlineData("codex", "Write numbers 1 to 60 | probe-folder", null)]
    [InlineData("opencode", "OpenCode", null)]
    [InlineData("opencode", "OC | Counting to 40 and lighthouse facts", null)]
    [InlineData("hermes", "Hermes", null)]
    [InlineData("hermes", "✓ gpt-6-sol-900k · …scratchpad\\probe-folder", false)]
    [InlineData("hermes", "✓ SIC-749c495e480d4eab9dc01f5… · gpt-6-sol-900k · …scratchpad\\probe-folder", false)]
    [InlineData("hermes", "⏳ SIC-749c495e480d4eab9dc01f5… · gpt-6-sol-900k · …scratchpad\\probe-folder", true)]
    public void RecordedTitlesAreRead(string agent, string title, bool? working)
    {
        Assert.Equal(working, AgentTitles.Working(agent, title));
    }

    [Theory]
    [InlineData("shell", "⏳ build")]
    [InlineData("shell", "✳ notes")]
    [InlineData("hermes", "✳ Claude Code")]
    [InlineData("claude", "✓ done")]
    [InlineData("claude", "✳Claude Code")]
    public void AGlyphMeansSomethingOnlyForTheAgentThatUsesItAndOnlyBeforeASpace(string agent, string title)
    {
        Assert.Null(AgentTitles.Working(agent, title));
    }

    [Fact]
    public void OutputMeansWorkingUntilThePaneHasBeenQuietForTheWindow()
    {
        var tracker = new PresenceTracker("shell");
        Assert.Equal(PaneState.Idle, tracker.State(At(0)));
        tracker.Output(At(1));
        Assert.Equal(PaneState.Working, tracker.State(At(1)));
        Assert.Equal(PaneState.Working, tracker.State(At(1) + PresenceTracker.QuietAfter - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(PaneState.Idle, tracker.State(At(1) + PresenceTracker.QuietAfter));
    }

    [Fact]
    public void ATurnThatEndsInABackgroundTabAsksForAttentionUntilTheTabIsSelected()
    {
        var tracker = new PresenceTracker("shell", selected: false);
        tracker.Input(At(-1));
        tracker.Output(At(0));
        Assert.Equal(PaneState.Working, tracker.State(At(1)));
        Assert.Equal(PaneState.Attention, tracker.State(At(10)));
        Assert.Equal(PaneState.Attention, tracker.State(At(60)));
        tracker.Selected(true, At(61));
        Assert.Equal(PaneState.Idle, tracker.State(At(61)));
    }

    [Fact]
    public void ATransitionIsFoundWhenTheStateIsNextAskedForEvenIfNothingAskedInBetween()
    {
        var tracker = new PresenceTracker("shell", selected: false);
        tracker.Input(At(-1));
        tracker.Output(At(0));
        Assert.Equal(PaneState.Attention, tracker.State(At(30)));
    }

    [Fact]
    public void ATurnThatEndedWhileTheTabWasSelectedStaysQuietWhenTheUserLeavesTheTab()
    {
        var tracker = new PresenceTracker("shell");
        tracker.Output(At(0));
        tracker.Selected(false, At(10));
        Assert.Equal(PaneState.Idle, tracker.State(At(20)));
    }

    [Fact]
    public void LeavingTheTabMidTurnFlagsTheEndOfThatTurn()
    {
        var tracker = new PresenceTracker("shell");
        tracker.Input(At(-1));
        tracker.Output(At(0));
        tracker.Selected(false, At(1));
        Assert.Equal(PaneState.Working, tracker.State(At(2)));
        Assert.Equal(PaneState.Attention, tracker.State(At(5)));
    }

    [Fact]
    public void AnIdleTitleWinsOverTheStatusBarTickHermesPrintsEverySecond()
    {
        var tracker = new PresenceTracker("hermes", selected: false);
        tracker.Title("✓ SIC-749c495e480d4eab9dc01f5… · gpt-6-sol-900k · …scratchpad\\probe-folder", At(18.9));
        for (var second = 19; second < 80; second++)
        {
            tracker.Output(At(second));
            Assert.Equal(PaneState.Idle, tracker.State(At(second + 0.5)));
        }
    }

    [Fact]
    public void AHermesTurnFollowsItsTitlesAndItsEndIsFlaggedInABackgroundTab()
    {
        var tracker = new PresenceTracker("hermes", selected: false);
        tracker.Title("✓ SIC-749c… · gpt-6-sol-900k · …probe-folder", At(18.9));
        tracker.Input(At(35));
        tracker.Title("⏳ SIC-749c… · gpt-6-sol-900k · …probe-folder", At(35.1));
        Assert.Equal(PaneState.Working, tracker.State(At(40)));
        Assert.Equal(PaneState.Working, tracker.State(At(43)));
        tracker.Title("✓ SIC-749c… · gpt-6-sol-900k · …probe-folder", At(43.3));
        Assert.Equal(PaneState.Attention, tracker.State(At(43.3)));
    }

    [Fact]
    public void AClaudePermissionPromptIsFlaggedThoughItsDotKeepsBlinking()
    {
        // The recorded sequence: working titles, then the idle glyph when the prompt appears, then a dot redrawn every 600 ms.
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Title("✳ Reply with ok", At(55));
        tracker.Input(At(101));
        tracker.Title("◑ Reply with ok", At(101.4));
        tracker.Output(At(101.5));
        tracker.Title("◐ Reply with ok", At(102.4));
        Assert.Equal(PaneState.Working, tracker.State(At(105)));
        tracker.Title("✳ Reply with ok", At(105.9));
        for (var blink = 106.0; blink < 200; blink += 0.6) tracker.Output(At(blink));
        Assert.Equal(PaneState.Attention, tracker.State(At(200)));
    }

    [Fact]
    public void ACodexTurnWithoutASpinnerTitleRidesOutItsLongestRecordedPause()
    {
        // Second turn of the recording: no title at all, output with a 2.3 s gap after 65.0 s, last output at 69.9 s.
        var tracker = new PresenceTracker("codex", selected: false);
        tracker.Title("Write numbers 1 to 60 | probe-folder", At(24.3));
        tracker.Input(At(60));
        tracker.Output(At(61.2));
        foreach (var second in new[] { 61.9, 63.1, 64.4, 65.0, 67.3, 68.2, 69.9 })
        {
            // Just before each chunk, including the one that ends the 2.3 s pause, the turn is still on.
            Assert.Equal(PaneState.Working, tracker.State(At(second - 0.01)));
            tracker.Output(At(second));
        }
        Assert.Equal(PaneState.Attention, tracker.State(At(69.9) + PresenceTracker.QuietAfter));
    }

    [Fact]
    public void ACodexSpinnerTitleHoldsWorkingAndTheTitleAfterItHandsBackToOutput()
    {
        var tracker = new PresenceTracker("codex");
        tracker.Title("⠋ | probe-folder", At(20.5));
        Assert.Equal(PaneState.Working, tracker.State(At(24)));
        tracker.Output(At(24.3));
        tracker.Title("Write numbers 1 to 60 | probe-folder", At(24.3));
        Assert.Equal(PaneState.Working, tracker.State(At(25)));
        Assert.Equal(PaneState.Idle, tracker.State(At(24.3) + PresenceTracker.QuietAfter));
    }

    [Fact]
    public void ABellAsksForAttentionOnlyInABackgroundTab()
    {
        var shown = new PresenceTracker("shell");
        shown.Bell(At(1));
        Assert.Equal(PaneState.Idle, shown.State(At(1)));
        var hidden = new PresenceTracker("shell", selected: false);
        hidden.Bell(At(1));
        Assert.Equal(PaneState.Attention, hidden.State(At(1)));
    }

    [Fact]
    public void AttentionOutranksANewTurnUntilTheTabIsSelected()
    {
        var tracker = new PresenceTracker("hermes", selected: false);
        tracker.Input(At(0));
        tracker.Title("⏳ turn one", At(0));
        tracker.Title("✓ turn one", At(5));
        tracker.Title("⏳ turn two", At(6));
        Assert.Equal(PaneState.Attention, tracker.State(At(7)));
        tracker.Selected(true, At(8));
        Assert.Equal(PaneState.Working, tracker.State(At(8)));
    }

    [Fact]
    public void AProcessThatEndsInABackgroundTabIsFlaggedAndNeverWorksAgain()
    {
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Title("◐ Topic", At(0));
        tracker.Exited(At(1));
        tracker.Output(At(1.1));
        Assert.Equal(PaneState.Attention, tracker.State(At(1.2)));
        tracker.Selected(true, At(2));
        Assert.Equal(PaneState.Idle, tracker.State(At(2)));
        var shown = new PresenceTracker("shell");
        shown.Output(At(0));
        shown.Exited(At(1));
        Assert.Equal(PaneState.Idle, shown.State(At(1)));
    }

    [Fact]
    public void ATurnTheUserStartedEndsWhenTheAgentComesToRest()
    {
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Title("✳ Claude Code", At(0));
        tracker.Input(At(10));
        tracker.Title("◐ Fix the build", At(10.2));
        tracker.Title("◑ Fix the build", At(11.2));
        Assert.Equal(0, tracker.TurnsEnded);
        tracker.Title("✳ Fix the build", At(40));
        Assert.Equal(1, tracker.TurnsEnded);
        Assert.Equal(PaneState.Attention, tracker.State(At(41)));
    }

    [Fact]
    public void ALaunchOrAResumeThatNobodyTypedIntoIsNoTurnAndSettlesQuietlyInABackgroundTab()
    {
        // Claude Code starting: output while its title says nothing, then the idle glyph; Hermes starting: its status bar, then ✓;
        // Codex and OpenCode, judged by output, drawing the conversation they brought back. Each was resumed in a background tab.
        var claude = new PresenceTracker("claude", selected: false);
        claude.Title("claude", At(0));
        for (var second = 0.1; second < 9; second += 0.3) claude.Output(At(second));
        claude.Title("✳ Claude Code", At(9));
        var hermes = new PresenceTracker("hermes", selected: false);
        hermes.Title("Hermes", At(0));
        for (var second = 0; second < 12; second++) hermes.Output(At(second));
        hermes.Title("✓ SIC-749c… · gpt-6-sol-900k · …probe-folder", At(12));
        var codex = new PresenceTracker("codex", selected: false);
        var opencode = new PresenceTracker("opencode", selected: false);
        for (var second = 0.2; second < 6; second += 0.4) { codex.Output(At(second)); opencode.Output(At(second)); }
        // A second burst after the first rest, as an agent finishing its start-up might draw, is no turn either.
        codex.Output(At(15));
        foreach (var tracker in new[] { claude, hermes, codex, opencode })
        {
            Assert.Equal(PaneState.Idle, tracker.State(At(30)));
            Assert.Equal(0, tracker.TurnsEnded);
        }
        // Once the user has typed into the pane, a turn that ends there out of view asks for attention as before.
        codex.Input(At(40));
        for (var second = 40.5; second < 52; second += 0.5) codex.Output(At(second));
        Assert.Equal(PaneState.Attention, codex.State(At(60)));
        Assert.Equal(1, codex.TurnsEnded);
    }

    [Fact]
    public void AQuickReplyReadAsItArrivesIsNoTurn()
    {
        var tracker = new PresenceTracker("hermes", selected: false);
        tracker.Title("✓ session · model · folder", At(0));
        tracker.Input(At(5));
        tracker.Title("⏳ session · model · folder", At(5.1));
        tracker.Title("✓ session · model · folder", At(5) + PresenceTracker.MinimumTurn - TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, tracker.TurnsEnded);
        tracker.Input(At(20));
        tracker.Title("⏳ session · model · folder", At(20.1));
        tracker.Title("✓ session · model · folder", At(20) + PresenceTracker.MinimumTurn);
        Assert.Equal(1, tracker.TurnsEnded);
    }

    [Fact]
    public void OnlyTheFirstRestAfterTheUserTypedCounts()
    {
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Input(At(0));
        tracker.Title("◐ Topic", At(0.2));
        tracker.Title("✳ Topic", At(30));
        tracker.Title("◐ Topic", At(31));
        tracker.Title("✳ Topic", At(60));
        Assert.Equal(1, tracker.TurnsEnded);
    }

    [Fact]
    public void AnApprovalPromptEndsATurnAndApprovingStartsAnother()
    {
        // The prompt reads as a rest; approving with a key starts the rest of the turn, whose end counts again.
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Input(At(0));
        tracker.Title("◐ Tidy the repo", At(0.3));
        tracker.Title("✳ Tidy the repo", At(12));
        for (var blink = 12.0; blink < 40; blink += 0.6) tracker.Output(At(blink));
        Assert.Equal(1, tracker.TurnsEnded);
        Assert.Equal(PaneState.Attention, tracker.State(At(40)));
        tracker.Input(At(41));
        tracker.Title("◐ Tidy the repo", At(41.2));
        tracker.Title("✳ Tidy the repo", At(70));
        Assert.Equal(2, tracker.TurnsEnded);
    }

    [Fact]
    public void TheEchoOfTypingIntoACodexPaneIsNoTurn()
    {
        // After its first turn Codex is judged by output, and every keystroke redraws its input box.
        var tracker = new PresenceTracker("codex");
        tracker.Title("Write numbers 1 to 60 | probe-folder", At(0));
        foreach (var second in new[] { 10.0, 10.4, 10.9, 11.3, 20.0, 20.5 })
        {
            tracker.Input(At(second));
            tracker.Output(At(second + 0.05));
        }
        Assert.Equal(PaneState.Idle, tracker.State(At(30)));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void ACodexTurnJudgedByOutputEndsWithItsLastOutput()
    {
        var tracker = new PresenceTracker("codex", selected: false);
        tracker.Title("Write numbers 1 to 60 | probe-folder", At(0));
        tracker.Input(At(60));
        foreach (var second in new[] { 61.2, 61.9, 63.1, 64.4, 65.0, 67.3, 68.2, 69.9 }) tracker.Output(At(second));
        Assert.Equal(0, tracker.TurnsEnded);
        // Asked about well after the quiet window, the rest is dated by the last output: 9.9 s of work after Enter.
        Assert.Equal(PaneState.Attention, tracker.State(At(80)));
        Assert.Equal(1, tracker.TurnsEnded);
    }

    [Fact]
    public void AShortBurstJudgedByOutputIsNoTurnThoughTheQuietWindowMakesItLookLonger()
    {
        var tracker = new PresenceTracker("codex");
        tracker.Input(At(0));
        tracker.Output(At(0.2));
        tracker.Output(At(2.5));
        Assert.Equal(PaneState.Idle, tracker.State(At(2.5) + PresenceTracker.QuietAfter));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void ARestReachedByExitingIsNoTurn()
    {
        var tracker = new PresenceTracker("claude");
        tracker.Input(At(0));
        tracker.Title("◐ Topic", At(0.2));
        tracker.Exited(At(30));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void APowerShellCommandIsNoTurn()
    {
        var tracker = new PresenceTracker("shell");
        tracker.Input(At(0));
        for (var second = 0.1; second < 20; second += 0.5) tracker.Output(At(second));
        Assert.Equal(PaneState.Idle, tracker.State(At(40)));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void ATurnThatEndsWhileWatchedNeitherRingsNorAsksForAttention()
    {
        var tracker = new PresenceTracker("claude");
        tracker.Input(At(0));
        tracker.Title("◐ Topic", At(0.2));
        tracker.Title("✳ Topic", At(30));
        Assert.Equal(0, tracker.TurnsEnded);
        Assert.Equal(PaneState.Idle, tracker.State(At(31)));
        // Watched while it worked, then left just before it ended: the end is out of view, so it rings and asks.
        tracker.Input(At(40));
        tracker.Title("◐ Topic", At(40.2));
        tracker.Selected(false, At(60));
        tracker.Title("✳ Topic", At(70));
        Assert.Equal(1, tracker.TurnsEnded);
        Assert.Equal(PaneState.Attention, tracker.State(At(71)));
    }

    [Theory]
    // Measured October 13, 2026: Codex 0.162.1 redraws 17 ms after the focus leaves its pane and when it comes back; OpenCode
    // 1 ms after it comes back.
    [InlineData("codex", 0.017)]
    [InlineData("opencode", 0.001)]
    public void ARedrawForAFocusChangeIsNoTurn(string agent, double after)
    {
        var tracker = new PresenceTracker(agent, selected: false);
        tracker.Sent(At(10));
        tracker.Output(At(10 + after));
        Assert.Equal(PaneState.Idle, tracker.State(At(10 + after)));
        Assert.Equal(PaneState.Idle, tracker.State(At(20)));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void OutputIsDatedByWhenItCameNotByWhenItWasReported()
    {
        // The page reports a burst at its start and once at its end, 750 ms on, with the latest output's age.
        var tracker = new PresenceTracker("codex", selected: false);
        tracker.Sent(At(10));
        tracker.Output(At(10.001));
        tracker.Output(At(10.751), TimeSpan.FromMilliseconds(734));
        Assert.Equal(PaneState.Idle, tracker.State(At(20)));
        // A command's echo, then its work half a second later, in the same report window: the work counts, as of when it came.
        tracker.Input(At(30));
        tracker.Output(At(30.01));
        tracker.Output(At(30.76), TimeSpan.FromMilliseconds(250));
        Assert.Equal(PaneState.Working, tracker.State(At(30.76)));
        Assert.Equal(PaneState.Working, tracker.State(At(30.51) + PresenceTracker.QuietAfter - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(PaneState.Attention, tracker.State(At(30.51) + PresenceTracker.QuietAfter));
    }

    [Fact]
    public void OutputThatOutlastsTheEchoIsWork()
    {
        var tracker = new PresenceTracker("opencode", selected: false);
        tracker.Input(At(0));
        tracker.Output(At(0.03));
        Assert.Equal(PaneState.Idle, tracker.State(At(0.03)));
        for (var second = 0.4; second < 12; second += 0.1) tracker.Output(At(second));
        Assert.Equal(PaneState.Working, tracker.State(At(12)));
        // Once working, what answers a report is part of the turn.
        tracker.Sent(At(12));
        tracker.Output(At(12.01));
        Assert.Equal(PaneState.Working, tracker.State(At(12.01) + PresenceTracker.QuietAfter - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(PaneState.Attention, tracker.State(At(20)));
        Assert.Equal(1, tracker.TurnsEnded);
    }

    // The agent's own status report (OSC 7501) outranks everything else: docs/decisions/program-status.md.

    [Fact]
    public void AReportedStateOutranksTitlesAndOutput()
    {
        var tracker = new PresenceTracker("claude");
        tracker.Report(new ProgramStatus(ProgramState.Working), At(0));
        tracker.Title("✳ Topic", At(0.1));
        Assert.Equal(PaneState.Working, tracker.State(At(10)));
        Assert.True(tracker.Reporting);
        tracker.Report(new ProgramStatus(ProgramState.Idle), At(11));
        for (var second = 11.0; second < 20; second += 0.5) tracker.Output(At(second));
        Assert.Equal(PaneState.Idle, tracker.State(At(20)));
    }

    [Fact]
    public void AReportedBlockedShowsInViewAndOutOfViewAndRingsOutOfView()
    {
        var shown = new PresenceTracker("claude");
        shown.Input(At(0));
        shown.Report(new ProgramStatus(ProgramState.Working), At(0.2));
        shown.Report(new ProgramStatus(ProgramState.Blocked, Kind: "permission"), At(5));
        Assert.Equal(PaneState.Blocked, shown.State(At(5)));
        Assert.Equal("permission", shown.BlockedKind);
        Assert.Equal(0, shown.TurnsEnded);
        var hidden = new PresenceTracker("claude", selected: false);
        hidden.Input(At(0));
        hidden.Report(new ProgramStatus(ProgramState.Working), At(0.2));
        hidden.Report(new ProgramStatus(ProgramState.Blocked, Kind: "question"), At(1));
        Assert.Equal(PaneState.Blocked, hidden.State(At(1)));
        Assert.Equal(1, hidden.TurnsEnded);
        // Coming into view answers nothing: blocked stays until the agent says otherwise.
        hidden.Selected(true, At(2));
        Assert.Equal(PaneState.Blocked, hidden.State(At(2)));
        hidden.Input(At(3));
        hidden.Report(new ProgramStatus(ProgramState.Working), At(3.1));
        Assert.Equal(PaneState.Working, hidden.State(At(3.1)));
        Assert.Null(hidden.BlockedKind);
    }

    [Fact]
    public void APromptAsTheAgentStartsRingsInABackgroundTabThoughNoTurnEnded()
    {
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Report(new ProgramStatus(ProgramState.Idle), At(0));
        tracker.Report(new ProgramStatus(ProgramState.Blocked, Kind: "question"), At(1));
        Assert.Equal(PaneState.Blocked, tracker.State(At(1)));
        Assert.Equal(1, tracker.TurnsEnded);
        // Said again, it is the same prompt.
        tracker.Report(new ProgramStatus(ProgramState.Blocked, Kind: "question"), At(2));
        Assert.Equal(1, tracker.TurnsEnded);
    }

    [Fact]
    public void AReportedDoneIsAnUnseenResultUntilThePaneComesIntoView()
    {
        var tracker = new PresenceTracker("codex", selected: false);
        tracker.Input(At(0));
        tracker.Report(new ProgramStatus(ProgramState.Working), At(0.1));
        // A reported end needs no minimum: the agent said so.
        tracker.Report(new ProgramStatus(ProgramState.Done), At(1));
        Assert.Equal(PaneState.Done, tracker.State(At(1)));
        Assert.Equal(1, tracker.TurnsEnded);
        Assert.Equal(PaneState.Done, tracker.State(At(60)));
        tracker.Selected(true, At(61));
        Assert.Equal(PaneState.Idle, tracker.State(At(61)));
        // In view, the result is seen as it arrives.
        tracker.Input(At(70));
        tracker.Report(new ProgramStatus(ProgramState.Working), At(70.1));
        tracker.Report(new ProgramStatus(ProgramState.Done), At(80));
        Assert.Equal(PaneState.Idle, tracker.State(At(80)));
        Assert.Equal(1, tracker.TurnsEnded);
    }

    [Fact]
    public void AReportedDoneNobodyAskedForShowsButDoesNotRing()
    {
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Report(new ProgramStatus(ProgramState.Working), At(0));
        tracker.Report(new ProgramStatus(ProgramState.Done), At(5));
        Assert.Equal(PaneState.Done, tracker.State(At(5)));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void AReportedIdleAfterWorkingIsJudgedAsATurnWasBefore()
    {
        // An agent that reports working and idle but no done (OpenCode 2.2): the end out of view asks for attention, and rings by the minimum.
        var tracker = new PresenceTracker("opencode", selected: false);
        tracker.Input(At(0));
        tracker.Report(new ProgramStatus(ProgramState.Working), At(0.1));
        tracker.Report(new ProgramStatus(ProgramState.Idle), At(1));
        Assert.Equal(PaneState.Attention, tracker.State(At(1)));
        Assert.Equal(0, tracker.TurnsEnded);
        tracker.Selected(true, At(2));
        tracker.Selected(false, At(3));
        tracker.Input(At(10));
        tracker.Report(new ProgramStatus(ProgramState.Working), At(10.1));
        tracker.Report(new ProgramStatus(ProgramState.Idle), At(10) + PresenceTracker.MinimumTurn);
        Assert.Equal(PaneState.Attention, tracker.State(At(20)));
        Assert.Equal(1, tracker.TurnsEnded);
    }

    [Fact]
    public void AResumeThatReportsWorkingThenIdleSettlesQuietlyInABackgroundTab()
    {
        var tracker = new PresenceTracker("claude", selected: false);
        tracker.Report(new ProgramStatus(ProgramState.Working), At(0));
        tracker.Report(new ProgramStatus(ProgramState.Idle), At(3));
        Assert.Equal(PaneState.Idle, tracker.State(At(3)));
        Assert.Equal(0, tracker.TurnsEnded);
    }

    [Fact]
    public void ErrorOutlivesTheProcessAndWorkingAndBlockedDoNot()
    {
        var failed = new PresenceTracker("claude", selected: false);
        failed.Input(At(0));
        failed.Report(new ProgramStatus(ProgramState.Working), At(0.1));
        failed.Report(new ProgramStatus(ProgramState.Error), At(5));
        Assert.Equal(PaneState.Error, failed.State(At(5)));
        Assert.Equal(1, failed.TurnsEnded);
        failed.Exited(At(6));
        Assert.Equal(PaneState.Error, failed.State(At(6)));
        failed.Selected(true, At(7));
        Assert.Equal(PaneState.Idle, failed.State(At(7)));
        var stopped = new PresenceTracker("claude", selected: false);
        stopped.Input(At(0));
        stopped.Report(new ProgramStatus(ProgramState.Blocked, Kind: "permission"), At(1));
        stopped.Exited(At(2));
        Assert.Equal(PaneState.Attention, stopped.State(At(2)));
        Assert.Null(stopped.BlockedKind);
    }

    [Fact]
    public void AClearedReportHandsBackToTitlesAndOutput()
    {
        var tracker = new PresenceTracker("codex");
        tracker.Report(new ProgramStatus(ProgramState.Idle), At(0));
        tracker.Output(At(1));
        Assert.Equal(PaneState.Idle, tracker.State(At(1)));
        tracker.Report(new ProgramStatus(ProgramState.Clear), At(2));
        Assert.False(tracker.Reporting);
        tracker.Output(At(3));
        Assert.Equal(PaneState.Working, tracker.State(At(3)));
        Assert.Equal(PaneState.Idle, tracker.State(At(3) + PresenceTracker.QuietAfter));
    }

    [Fact]
    public void ASubRecordDoesNotSpeakForThePane()
    {
        var tracker = new PresenceTracker("claude");
        tracker.Report(new ProgramStatus(ProgramState.Working, Id: "build/test"), At(0));
        Assert.Equal(PaneState.Idle, tracker.State(At(0)));
        Assert.False(tracker.Reporting);
    }

    [Fact]
    public void BlockedOutranksTheRestAndWorkingOutranksIdle()
    {
        // A tab of several panes shows its most urgent pane, so the states rise in that order.
        Assert.True(PaneState.Blocked > PaneState.Attention && PaneState.Attention > PaneState.Error && PaneState.Error > PaneState.Done
            && PaneState.Done > PaneState.Working && PaneState.Working > PaneState.Idle);
    }

    [Theory]
    [InlineData("h", true)]
    [InlineData("\r", true)]
    [InlineData("1", true)]
    [InlineData("\u0003", true)]
    [InlineData("\u007f", true)]
    [InlineData("", false)]
    [InlineData("\u001b[I", false)]
    [InlineData("\u001b[O", false)]
    [InlineData("\u001b[?1;2c", false)]
    [InlineData("\u001b[12;40R", false)]
    [InlineData("\u001b]11;rgb:0101/1e1e/2626\u001b\\", false)]
    [InlineData("\u001b[A", false)]
    [InlineData("\u001b[200~pasted\u001b[201~", false)]
    public void TypedTextAndEnterCountAsInputAndTheTerminalsOwnAnswersDoNot(string data, bool expected) => Assert.Equal(expected, PresenceTracker.IsUserInput(data));
}
