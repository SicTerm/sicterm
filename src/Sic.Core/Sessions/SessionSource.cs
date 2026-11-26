using Microsoft.Data.Sqlite;

namespace Sic.Core.Sessions;

/// <summary>
/// What an import of an agent's own database produced: the sessions whose messages were read, each with the fingerprint of
/// what was read, and how many were left unread because the fingerprint given for them was the one the database shows now.
/// <see cref="Unlisted"/> holds the ids of the sessions left out because their id is in a form SicTerm does not resume
/// (docs/decisions/unlisted-sessions.md).
/// </summary>
public sealed record ImportedSessions(IReadOnlyList<(Conversation Conversation, string Fingerprint)> Imported, int Unchanged, IReadOnlyList<string>? Unlisted = null);

/// <summary>What the two SQLite history readers (Hermes, OpenCode) share.</summary>
internal static class SqliteHistory
{
    /// <summary>The agent's database, read-only, with a short wait for a lock; the agent may be writing to it.</summary>
    internal static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
        }.ToString());
        try { connection.Open(); }
        catch { connection.Dispose(); throw; }
        return connection;
    }

    internal static HashSet<string> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(" + table + ")";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    /// <summary>
    /// A fingerprint per session of its messages as a whole, from one pass over the messages table: how many there are, the
    /// last one's order, and their total length, so a message added, taken out or changed in place changes it. Sessions with
    /// no messages are absent.
    /// </summary>
    internal static Dictionary<string, string> MessageFingerprints(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            result[reader.GetString(0)] = string.Join(':', Enumerable.Range(1, reader.FieldCount - 1).Select(column => reader.IsDBNull(column) ? "" : reader.GetValue(column).ToString()));
        }
        return result;
    }
}

/// <summary>The title a conversation is listed under, the same for every source.</summary>
public static class ConversationTitles
{
    /// <summary>
    /// Whether a request is one a title can be made from: not a slash-command record or tool XML, which start with '/' or '<'
    /// (Claude Code's /clear, say, which is the first message of many a transcript). The readers pass the first such request.
    /// </summary>
    public static bool IsMeaningful(string? text) => text?.TrimStart() is { Length: > 0 } lead && !lead.StartsWith('<') && !lead.StartsWith('/') && !IsAgentPreamble(lead);

    /// <summary>
    /// Whether a request is one the agent wrote into the transcript itself, not the person: Codex opens a rollout with its
    /// environment block (&lt;environment_context&gt;) and the folder's AGENTS.md ("# AGENTS.md instructions for C:\repo", then
    /// the file) as user messages, before the first real request. Such a message never titles a conversation, not even as the
    /// last resort a slash-command record is.
    /// </summary>
    public static bool IsAgentPreamble(string? text) => text?.TrimStart() is { } lead
        && (lead.StartsWith("# AGENTS.md instructions for", StringComparison.Ordinal) || lead.StartsWith("<environment_context>", StringComparison.Ordinal));

    /// <summary>
    /// The conversation's own title; else the first line of its first request, a slash-command record reading as the command
    /// itself ("/clear" for a transcript that holds nothing but one); else "Untitled conversation". Bounded to 120 characters.
    /// </summary>
    public static string Resolve(string? title, string? firstUser)
    {
        var resolved = string.IsNullOrWhiteSpace(title)
            ? firstUser?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            : title;
        if (resolved is not null && System.Text.RegularExpressions.Regex.Match(resolved, "^<command-name>(.+?)</command-name>") is { Success: true } command) resolved = command.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(resolved)) resolved = "Untitled conversation";
        return resolved.Length > 120 ? resolved[..120] + "…" : resolved;
    }
}
