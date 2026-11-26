using Sic.Core.Agents;
using Sic.Core.State;

namespace Sic.Core.Sessions;

/// <summary>
/// A session started in a pane's folder in place of a resume. Two cases call for one:
/// <list type="bullet">
/// <item>A session SIC started whose conversation the agent never saved (<see cref="SavedPane.Unsaved"/>), as when a new
/// Claude Code session is closed before its first message: Claude Code writes its transcript only then, and the exact resume
/// of the ID SIC gave it found nothing ("The authoritative saved history was not found"). Such a pane starts again under the
/// same identity, since nothing was lost: Claude Code takes the same <c>--session-id</c> and OpenCode's <c>--session</c>
/// creates the session when it does not exist. Codex links on its first message as before, and Hermes once it has written
/// the new session's row, a few seconds after it starts (docs/decisions/hermes-session-names.md).</item>
/// <item>A saved conversation whose history is gone, or a pane linked to none, when the user chooses Start new session here.
/// That never happens on its own: a blank session in a real conversation's place would pass for the conversation. It gets a
/// new identity, so the old conversation is not taken over should its history turn up again.</item>
/// </list>
/// </summary>
public static class FreshStart
{
    /// <summary>
    /// The identity a new session of <paramref name="agentId"/> is started under: the ID SIC assigns (Claude Code, OpenCode);
    /// none for Codex, linked on its first message, for Hermes, linked once it has written the session it names itself
    /// (docs/decisions/hermes-session-names.md), or for a shell. No new session gets a creation name since November 2026: that
    /// was the title SIC gave a new Hermes session, which kept Hermes from naming it, and a pane saved with one still finds
    /// its session by it.
    /// </summary>
    public static (string? SessionId, string? CreationName) NewIdentity(string agentId, DateTimeOffset now) => agentId switch
    {
        "claude" => (Guid.NewGuid().ToString(), null),
        "opencode" => (OpenCodeSessionIds.New(now), null),
        _ => (null, null)
    };

    /// <summary>
    /// Whether a resume of <paramref name="pane"/> starts its session again instead: it is unsaved and its history has no
    /// conversation under its identity, or it has no identity to look for. A transcript that names another session is still an error.
    /// </summary>
    public static bool Replaces(SavedPane pane, ResumeVerdict? verdict) =>
        pane.Unsaved && pane.AgentId != "shell" && verdict is null or ResumeVerdict.SourceMissing or ResumeVerdict.SessionMissing;

    /// <summary>
    /// Whether the placeholder offers Start new session here: an agent pane that cannot resume, because its history was not
    /// found (<paramref name="historyMissing"/>) or it is linked to no conversation. An unsaved pane needs no offer; it starts again on Resume.
    /// </summary>
    public static bool Offered(SavedPane pane, bool historyMissing) =>
        pane.AgentId != "shell" && !pane.Unsaved && (historyMissing || pane.SessionId is null && pane.CreationName is null);

    /// <summary>The pane as a new session in its folder, under a new identity, named as a new session is (<paramref name="title"/>).</summary>
    public static SavedPane Renewed(SavedPane pane, string title, string historyDirectory, DateTimeOffset now)
    {
        var (sessionId, creationName) = NewIdentity(pane.AgentId, now);
        return pane with { SessionId = sessionId, CreationName = creationName, Title = title, HistoryDirectory = historyDirectory, Unsaved = true };
    }
}
