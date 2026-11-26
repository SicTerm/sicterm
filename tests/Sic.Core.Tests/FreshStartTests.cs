using Sic.Core.Agents;
using Sic.Core.Sessions;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class FreshStartTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static SavedPane Pane(string agent, string? sessionId = null, string? creationName = null, bool unsaved = false) =>
        new("p1", agent, "Claude Code · docs", @"C:\Work\docs", @"C:\Users\Avery\.claude", sessionId, creationName, Unsaved: unsaved);

    [Fact]
    public void EachAgentGetsTheIdentityItsLaunchNeeds()
    {
        var (claude, claudeName) = FreshStart.NewIdentity("claude", Now);
        Assert.True(AgentLaunchBuilder.IsExactSessionId("claude", claude!));
        Assert.Null(claudeName);
        var (openCode, _) = FreshStart.NewIdentity("opencode", Now);
        Assert.True(AgentLaunchBuilder.IsExactSessionId("opencode", openCode!));
        // Hermes names a new session itself, and the pane is linked to it once Hermes has written it.
        Assert.Equal((null, null), FreshStart.NewIdentity("hermes", Now));
        Assert.Equal((null, null), FreshStart.NewIdentity("codex", Now));
        Assert.Equal((null, null), FreshStart.NewIdentity("shell", Now));
    }

    [Fact]
    public void AnUnsavedSessionWithNoHistoryStartsAgain()
    {
        var unsaved = Pane("claude", Guid.NewGuid().ToString(), unsaved: true);
        Assert.True(FreshStart.Replaces(unsaved, ResumeVerdict.SourceMissing));
        Assert.True(FreshStart.Replaces(unsaved, ResumeVerdict.SessionMissing));
        // Codex has no ID until its first message, so there is no history to look for.
        Assert.True(FreshStart.Replaces(Pane("codex", unsaved: true), null));
        // A transcript that names another session is not a missing one.
        Assert.False(FreshStart.Replaces(unsaved, ResumeVerdict.IdentityChanged));
    }

    [Fact]
    public void ASavedConversationIsNeverReplacedOnItsOwn()
    {
        var saved = Pane("claude", Guid.NewGuid().ToString());
        Assert.False(FreshStart.Replaces(saved, ResumeVerdict.SourceMissing));
        Assert.False(FreshStart.Replaces(saved, ResumeVerdict.SessionMissing));
        Assert.False(FreshStart.Replaces(Pane("shell", unsaved: true), null));
    }

    [Fact]
    public void StartNewSessionHereIsOfferedWhereAResumeCannotGo()
    {
        Assert.True(FreshStart.Offered(Pane("claude", Guid.NewGuid().ToString()), historyMissing: true));
        Assert.False(FreshStart.Offered(Pane("claude", Guid.NewGuid().ToString()), historyMissing: false));
        // Linked to nothing: an older pane closed before its first message, or one never linked.
        Assert.True(FreshStart.Offered(Pane("codex"), historyMissing: false));
        // A pane an older SicTerm saved under a creation name still finds its session by it.
        Assert.False(FreshStart.Offered(Pane("hermes", creationName: "SIC-0123456789abcdef0123456789abcdef"), historyMissing: false));
        // An unsaved pane starts again from its own button, and a shell always starts.
        Assert.False(FreshStart.Offered(Pane("claude", Guid.NewGuid().ToString(), unsaved: true), historyMissing: true));
        Assert.False(FreshStart.Offered(Pane("shell"), historyMissing: true));
    }

    [Fact]
    public void ARenewedPaneIsANewUnsavedSessionInTheSameFolder()
    {
        var old = Pane("claude", Guid.NewGuid().ToString()) with { CustomTitle = "Docs work" };
        var renewed = FreshStart.Renewed(old, "Claude Code · docs", @"D:\claude-home", Now);
        Assert.NotEqual(old.SessionId, renewed.SessionId);
        Assert.True(AgentLaunchBuilder.IsExactSessionId("claude", renewed.SessionId!));
        Assert.True(renewed.Unsaved);
        Assert.Equal((old.Id, old.WorkingDirectory, "Docs work"), (renewed.Id, renewed.WorkingDirectory, renewed.CustomTitle));
        Assert.Equal(@"D:\claude-home", renewed.HistoryDirectory);
        var hermes = FreshStart.Renewed(Pane("hermes", "20261014_120000_abcdef", "SIC-0123456789abcdef0123456789abcdef"), "Hermes · docs", @"C:\hermes", Now);
        Assert.Null(hermes.SessionId);
        Assert.Null(hermes.CreationName);
        Assert.True(hermes.Unsaved);
    }

    [Fact]
    public void TheMarkSurvivesTheLayoutFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "sic-fresh-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonStateStore<WorkspaceLayout>(path);
            store.Save(new WorkspaceLayout { Tabs = [new SavedTab("t1", "Claude Code · docs", [Pane("claude", Guid.NewGuid().ToString(), unsaved: true), Pane("codex")])] });
            var panes = store.Load(() => new WorkspaceLayout()).Tabs[0].Panes;
            Assert.True(panes[0].Unsaved);
            Assert.False(panes[1].Unsaved);
        }
        finally { File.Delete(path); }
    }
}
