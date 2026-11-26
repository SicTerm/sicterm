using Sic.Core.Agents;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Sic.Core.Sessions;

/// <summary>
/// Reads Hermes's history: state.db in the history home, a row per session in sessions and a row per message in messages.
/// Only terminal sessions are read (<see cref="TerminalSessions"/>); a gateway's are not. Each session's import carries a
/// fingerprint of the session row and its messages, so a later import reads the messages only of the sessions that changed.
/// A session with no request or reply yet is not imported: Hermes writes a session's row when it begins and deletes the row
/// of an untitled one that ends with no messages, so it is not a conversation until its first message, as a Claude Code
/// session is not before its transcript (docs/decisions/hermes-session-names.md).
/// </summary>
public static class HermesHistoryReader
{
    private sealed record Listed(Conversation Header, string Fingerprint);

    /// <summary>Every terminal session with its messages, newest first.</summary>
    public static IReadOnlyList<Conversation> Read(AgentDefinition agent) => Read(agent, _ => null).Imported.Select(item => item.Conversation).ToList();

    /// <summary>
    /// The terminal sessions whose fingerprint is not what <paramref name="known"/> gives for their id, with their messages;
    /// the rest are counted, not read.
    /// </summary>
    public static ImportedSessions Read(AgentDefinition agent, Func<string, string?> known)
    {
        var path = Path.Combine(agent.HistoryDirectory, "state.db");
        if (!File.Exists(path)) return new([], 0);
        using var connection = SqliteHistory.OpenReadOnly(path);
        var messageColumns = RequireSchema(connection, out var sessionColumns);
        var imported = new List<(Conversation, string)>();
        var unchanged = 0;
        var unlisted = new List<string>();
        foreach (var listed in List(connection, agent, path, sessionColumns, messageColumns, unlisted))
        {
            if (known(listed.Header.SessionId) == listed.Fingerprint) { unchanged++; continue; }
            imported.Add((Load(connection, listed.Header, messageColumns), listed.Fingerprint));
        }
        return new(imported, unchanged, unlisted);
    }

    /// <summary>
    /// The terminal sessions whose title, as <see cref="Read"/> resolves it, is <paramref name="title"/>: the name an older
    /// SicTerm gave a new session, which Hermes wrote as its title, or, for a session without a title, the first line of its
    /// first request. Only the sessions that may match have their messages read: those titled so, and the untitled ones with a
    /// request that holds the name. A pane saved with such a name and never linked finds its session by it on resume, so it
    /// lists no fingerprints and reads no session it can rule out. New sessions are found by <see cref="FindCreated"/>.
    /// </summary>
    public static IReadOnlyList<Conversation> ReadByTitle(AgentDefinition agent, string title)
    {
        var path = Path.Combine(agent.HistoryDirectory, "state.db");
        if (!File.Exists(path)) return [];
        using var connection = SqliteHistory.OpenReadOnly(path);
        var messageColumns = RequireSchema(connection, out var sessionColumns);
        var mentioning = SessionsMentioning(connection, title);
        return Headers(connection, agent, path, sessionColumns, title, unlisted: null)
            .Where(header => header.Title == title || string.IsNullOrWhiteSpace(header.Title) && (mentioning is null || mentioning.Contains(header.SessionId)))
            .Select(header => Load(connection, header, messageColumns))
            .Where(conversation => conversation.Title == title)
            .ToList();
    }

    /// <summary>
    /// The terminal sessions Hermes began in <paramref name="directory"/> at or after <paramref name="from"/>, and before
    /// <paramref name="until"/> when it is given, with their messages: how a pane finds the new session it started, since
    /// SicTerm starts one untitled and lets Hermes name it (docs/decisions/hermes-session-names.md). Hermes writes the row,
    /// with the folder, when the session begins, a few seconds after its interface starts and before anything is sent. A
    /// session whose folder Hermes did not record is not one of them. Only new sessions are listed and read, so a pane can ask
    /// once a second.
    /// </summary>
    public static IReadOnlyList<Conversation> FindCreated(AgentDefinition agent, string directory, DateTimeOffset from, DateTimeOffset? until = null)
    {
        var path = Path.Combine(agent.HistoryDirectory, "state.db");
        if (!File.Exists(path)) return [];
        using var connection = SqliteHistory.OpenReadOnly(path);
        var messageColumns = RequireSchema(connection, out var sessionColumns);
        if (!sessionColumns.Contains("cwd")) return [];
        return Headers(connection, agent, path, sessionColumns, title: null, unlisted: null, from, until)
            .Where(header => FolderIdentity.Same(header.WorkingDirectory, directory))
            .Select(header => Load(connection, header, messageColumns))
            .ToList();
    }

    /// <summary>
    /// The sessions with a request that holds <paramref name="title"/> as written, which an untitled session must have to be
    /// titled so; null, meaning any, for a title the JSON Hermes keeps some requests in might write otherwise (anything but
    /// ASCII letters, digits, spaces, hyphens, underscores and dots), when every untitled session is read as before. The names
    /// SIC gives new sessions (SIC-, and CommandCentral- before it, then hex) are always of the first kind.
    /// </summary>
    private static HashSet<string>? SessionsMentioning(SqliteConnection connection, string title)
    {
        if (!title.All(character => char.IsAsciiLetterOrDigit(character) || character is ' ' or '-' or '_' or '.')) return null;
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT DISTINCT session_id FROM messages WHERE role='user' AND instr(content,$title)>0";
        query.Parameters.AddWithValue("$title", title);
        var sessions = new HashSet<string>(StringComparer.Ordinal);
        using var rows = query.ExecuteReader();
        while (rows.Read()) if (!rows.IsDBNull(0)) sessions.Add(rows.GetString(0));
        return sessions;
    }

    /// <summary>
    /// Whether the history home holds the terminal session <paramref name="sessionId"/>, the kind <see cref="Read"/> imports.
    /// One indexed row lookup, where <see cref="Read"/> lists every session: this is what an exact resume checks.
    /// </summary>
    public static bool Exists(AgentDefinition agent, string sessionId)
    {
        var path = Path.Combine(agent.HistoryDirectory, "state.db");
        if (!File.Exists(path)) return false;
        using var connection = SqliteHistory.OpenReadOnly(path);
        var sessionColumns = SqliteHistory.Columns(connection, "sessions");
        if (!new[] { "id", "source" }.All(sessionColumns.Contains))
            throw new InvalidDataException("This Hermes history schema is not supported; the source was not changed.");
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT 1 FROM sessions WHERE id=$id AND " + TerminalSessions(sessionColumns) + " LIMIT 1";
        query.Parameters.AddWithValue("$id", sessionId);
        return query.ExecuteScalar() is not null;
    }

    /// <summary>
    /// The SQL that keeps a terminal session: one begun from Hermes's command line, its TUI or its desktop app. Since Hermes's
    /// unified runtime (v0.22.7, seen November 3, 2026) the gateway rewrites a local session's <c>source</c> to <c>local</c>
    /// at its first turn and keeps the surface it was created from in <c>created_source</c> (cli, tui, desktop; acp for an
    /// editor's, tool and oneshot for scripted runs; NULL or unknown for a row from before the column was filled), so the
    /// creation surface decides where it is known and the source otherwise, <c>local</c> counted as a terminal's
    /// (docs/decisions/hermes-local-sessions.md). A messaging gateway's session (telegram, discord, …) is still not read.
    /// </summary>
    private static string TerminalSessions(HashSet<string> sessionColumns) => sessionColumns.Contains("created_source")
        ? "(CASE WHEN created_source IN ('cli','tui','desktop') THEN 1 WHEN created_source IS NULL OR created_source IN ('','unknown') THEN source IN ('cli','tui','desktop','local') ELSE 0 END)"
        : "source IN ('cli','tui','desktop','local')";

    private static HashSet<string> RequireSchema(SqliteConnection connection, out HashSet<string> sessionColumns)
    {
        sessionColumns = SqliteHistory.Columns(connection, "sessions");
        var messageColumns = SqliteHistory.Columns(connection, "messages");
        if (!new[] { "id", "source", "started_at" }.All(sessionColumns.Contains) ||
            !new[] { "session_id", "role", "content", "id" }.All(messageColumns.Contains))
            throw new InvalidDataException("This Hermes history schema is not supported; the source was not changed.");
        return messageColumns;
    }

    /// <summary>The terminal sessions with a request or reply, newest first, each without its messages and with its fingerprint.</summary>
    private static List<Listed> List(SqliteConnection connection, AgentDefinition agent, string path, HashSet<string> sessionColumns, HashSet<string> messageColumns, List<string> unlisted)
    {
        var messages = SqliteHistory.MessageFingerprints(connection, "SELECT session_id,COUNT(*),MAX(id),SUM(LENGTH(content))"
            + (messageColumns.Contains("active") ? ",SUM(COALESCE(active,1))" : "") + " FROM messages WHERE role IN ('user','assistant') GROUP BY session_id");
        return Headers(connection, agent, path, sessionColumns, title: null, unlisted)
            .Where(header => messages.ContainsKey(header.SessionId))
            .Select(header => new Listed(header, string.Join('|', header.Title, header.WorkingDirectory, header.StartedAt.UtcTicks, header.UpdatedAt.UtcTicks, messages[header.SessionId])))
            .ToList();
    }

    /// <summary>
    /// The terminal sessions' records, newest first, without their messages: every one, or with <paramref name="title"/> those
    /// titled so and the untitled, or with <paramref name="from"/> those begun since then (and before <paramref name="until"/>).
    /// A session whose id is in a form SicTerm does not resume is left out, its id added to <paramref name="unlisted"/>; the
    /// rest are read as usual (docs/decisions/unlisted-sessions.md).
    /// </summary>
    private static List<Conversation> Headers(SqliteConnection connection, AgentDefinition agent, string path, HashSet<string> sessionColumns, string? title, List<string>? unlisted,
        DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        var cwd = sessionColumns.Contains("cwd") ? "cwd" : "NULL";
        var titled = sessionColumns.Contains("title") ? "title" : "NULL";
        var updated = sessionColumns.Contains("last_activity_at") ? "COALESCE(last_activity_at,started_at)" : "started_at";
        using var query = connection.CreateCommand();
        query.CommandText = $"SELECT id,{cwd},{titled},started_at,{updated} FROM sessions WHERE {TerminalSessions(sessionColumns)}"
            + (title is null ? "" : $" AND ({titled}=$title OR {titled} IS NULL OR trim({titled})='')")
            + (from is null ? "" : " AND started_at>=$from") + (until is null ? "" : " AND started_at<$until") + " ORDER BY started_at DESC";
        if (title is not null) query.Parameters.AddWithValue("$title", title);
        // Hermes keeps started_at as seconds since 1970, with a fraction.
        if (from is { } since) query.Parameters.AddWithValue("$from", since.ToUnixTimeMilliseconds() / 1000.0);
        if (until is { } before) query.Parameters.AddWithValue("$until", before.ToUnixTimeMilliseconds() / 1000.0);
        var headers = new List<Conversation>();
        using var rows = query.ExecuteReader();
        while (rows.Read())
        {
            var id = rows.GetString(0);
            if (!AgentLaunchBuilder.IsExactSessionId("hermes", id)) { unlisted?.Add(id); continue; }
            headers.Add(new Conversation(agent.Id, id, rows.IsDBNull(2) ? "" : rows.GetString(2),
                rows.IsDBNull(1) ? null : rows.GetString(1),
                DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(rows.GetDouble(3) * 1000))),
                DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(rows.GetDouble(4) * 1000))), path, agent.HistoryDirectory, ""));
        }
        return headers;
    }

    /// <summary>The session with its messages read and its title resolved.</summary>
    private static Conversation Load(SqliteConnection connection, Conversation header, HashSet<string> messageColumns)
    {
        using var messages = connection.CreateCommand();
        messages.CommandText = "SELECT role,content FROM messages WHERE session_id=$id AND role IN ('user','assistant')" +
            (messageColumns.Contains("active") ? " AND COALESCE(active,1)=1" : "") + " ORDER BY id";
        messages.Parameters.AddWithValue("$id", header.SessionId);
        var text = new List<string>();
        string? firstUser = null, firstMeaningful = null;
        using var reader = messages.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1)) continue;
            var content = reader.GetString(1);
            if (content.TrimStart().StartsWith('['))
            {
                try
                {
                    using var document = JsonDocument.Parse(content);
                    content = TranscriptReader.ReadText(document.RootElement);
                }
                catch (JsonException) { /* Plain text beginning with '[' is still valid message text. */ }
            }
            if (string.IsNullOrWhiteSpace(content)) continue;
            var role = reader.GetString(0);
            if (role == "user") { firstUser ??= content; if (firstMeaningful is null && ConversationTitles.IsMeaningful(content)) firstMeaningful = content; }
            text.Add((role == "user" ? "You" : "Hermes") + "\n" + content);
        }
        return header with { Title = ConversationTitles.Resolve(header.Title, firstMeaningful ?? firstUser), Body = string.Join("\n\n", text) };
    }
}
