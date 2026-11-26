using Sic.Core.Agents;

namespace Sic.Core.Sessions;

/// <summary>What the check of a conversation's saved history before an exact resume found.</summary>
public enum ResumeVerdict { Ready, SourceMissing, SessionMissing, IdentityChanged }

/// <summary>
/// The check an exact resume makes of the authoritative saved history before anything starts: the catalog's entry for the
/// conversation, its source on disk, and the session's own identity read from that source (the transcript's first record
/// that names its session for Claude Code and Codex, the session's row for Hermes and OpenCode). It keeps no message text and needs no catalog refresh, so a resume
/// of a conversation the catalog already records no longer waits for one; on a real history the
/// refresh took 5.8–6.7 s whenever Hermes had written to its database, and 14.6 s the first time after launch. Since
/// October 16, 2026 the transcript is read only as far as its first session ID (<see cref="TranscriptReader.ReadSessionId"/>):
/// the whole-file scan it replaced took 90–270 ms per large transcript, paid tab by tab in Resume all. A verdict other
/// than <see cref="ResumeVerdict.Ready"/> is worth one refresh and a second check, since the refresh finds a source that
/// moved or appeared since the catalog was last read; after that, <see cref="Describe"/> is the error.
/// </summary>
public static class ResumeSource
{
    /// <summary>
    /// Checks <paramref name="entry"/>, the catalog's record of <paramref name="sessionId"/> (null when it has none), against
    /// the source it names. A transcript that cannot be read, or that has no usable session ID, throws as it does on import.
    /// A Hermes session is checked by its row alone, entry or none: every Hermes session is in its home's state.db, and one
    /// with no message yet is not in the catalog (docs/decisions/hermes-session-names.md).
    /// </summary>
    public static ResumeVerdict Check(AgentDefinition agent, Conversation? entry, string sessionId)
    {
        if (agent.Id == "hermes") return HermesHistoryReader.Exists(agent, sessionId) ? ResumeVerdict.Ready : ResumeVerdict.SessionMissing;
        if (entry is null || !File.Exists(entry.SourcePath)) return ResumeVerdict.SourceMissing;
        if (agent.Id == "opencode") return OpenCodeHistoryReader.Exists(agent, sessionId) ? ResumeVerdict.Ready : ResumeVerdict.SessionMissing;
        return TranscriptReader.ReadSessionId(agent, entry.SourcePath) == sessionId ? ResumeVerdict.Ready : ResumeVerdict.IdentityChanged;
    }

    /// <summary>The error for a verdict that still stands after a refresh. No replacement conversation is started for any of them.</summary>
    public static string Describe(ResumeVerdict verdict) => verdict switch
    {
        ResumeVerdict.SourceMissing => "The saved conversation was not found. No new conversation was started.",
        ResumeVerdict.SessionMissing => "The session no longer exists in the agent's history.",
        ResumeVerdict.IdentityChanged => "The saved conversation's file now holds a different session.",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "A ready source has nothing to describe.")
    };
}
