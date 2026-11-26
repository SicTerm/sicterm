using Sic.Core.Agents;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Sic.Core.Sessions;

/// <summary>
/// Reads OpenCode 2.0's history: one SQLite database, opencode.db in the history home, with a row per session in session_v2
/// and a JSON row per message in session_message. Only top-level sessions are read, as OpenCode's own session list shows
/// them; a session with a parent is a subagent's, run inside another session's turn, and cannot be resumed on its own. Each
/// session's import carries a fingerprint of the session row and its messages, so a later import reads the messages only of
/// the sessions that changed.
/// </summary>
public static class OpenCodeHistoryReader
{
    public const string DatabaseName = "opencode.db";

    private sealed record Listed(Conversation Header, string Fingerprint);

    /// <summary>Every top-level session with its messages, newest first.</summary>
    public static IReadOnlyList<Conversation> Read(AgentDefinition agent) => Read(agent, _ => null).Imported.Select(item => item.Conversation).ToList();

    /// <summary>
    /// The top-level sessions whose fingerprint is not what <paramref name="known"/> gives for their id, with their messages;
    /// the rest are counted, not read.
    /// </summary>
    public static ImportedSessions Read(AgentDefinition agent, Func<string, string?> known)
    {
        var path = Path.Combine(agent.HistoryDirectory, DatabaseName);
        if (!File.Exists(path)) return new([], 0);
        using var connection = SqliteHistory.OpenReadOnly(path);
        RequireSchema(connection);
        var imported = new List<(Conversation, string)>();
        var unchanged = 0;
        var unlisted = new List<string>();
        foreach (var listed in List(connection, agent, path, unlisted))
        {
            if (known(listed.Header.SessionId) == listed.Fingerprint) { unchanged++; continue; }
            imported.Add((Load(connection, agent, listed.Header), listed.Fingerprint));
        }
        return new(imported, unchanged, unlisted);
    }

    /// <summary>
    /// Whether the history home holds the top-level session <paramref name="sessionId"/>, the kind <see cref="Read"/> imports.
    /// One indexed row lookup, where <see cref="Read"/> lists every session: this is what an exact resume checks.
    /// </summary>
    public static bool Exists(AgentDefinition agent, string sessionId)
    {
        var path = Path.Combine(agent.HistoryDirectory, DatabaseName);
        if (!File.Exists(path)) return false;
        using var connection = SqliteHistory.OpenReadOnly(path);
        RequireSchema(connection);
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT 1 FROM session_v2 WHERE id=$id AND parent_id IS NULL LIMIT 1";
        query.Parameters.AddWithValue("$id", sessionId);
        return query.ExecuteScalar() is not null;
    }

    /// <summary>
    /// The provider of the session's model (session_v2.model's <c>providerID</c>, "openai" for an OpenAI login or key), or null
    /// when the session is not there yet or names none. One row of the sessions table; the account and credential tables are
    /// never read. The usage limits use it to tell an OpenCode pane on the ChatGPT plan (docs/decisions/usage-limits.md).
    /// </summary>
    public static string? Provider(AgentDefinition agent, string sessionId)
    {
        var path = Path.Combine(agent.HistoryDirectory, DatabaseName);
        if (!File.Exists(path)) return null;
        using var connection = SqliteHistory.OpenReadOnly(path);
        RequireSchema(connection);
        if (!SqliteHistory.Columns(connection, "session_v2").Contains("model")) return null;
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT model FROM session_v2 WHERE id=$id LIMIT 1";
        query.Parameters.AddWithValue("$id", sessionId);
        if (query.ExecuteScalar() is not string model) return null;
        try
        {
            using var document = JsonDocument.Parse(model);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("providerID", out var provider)
                && provider.ValueKind == JsonValueKind.String ? provider.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static void RequireSchema(SqliteConnection connection)
    {
        if (!new[] { "id", "parent_id", "directory", "title", "time_created", "time_updated" }.All(SqliteHistory.Columns(connection, "session_v2").Contains) ||
            !new[] { "session_id", "type", "seq", "data" }.All(SqliteHistory.Columns(connection, "session_message").Contains))
            throw new InvalidDataException("This OpenCode history schema is not supported; the source was not changed.");
    }

    /// <summary>
    /// The top-level sessions, newest first, each without its messages and with its fingerprint. A session whose id is in a form
    /// SicTerm does not resume is left out, its id added to <paramref name="unlisted"/> (docs/decisions/unlisted-sessions.md).
    /// </summary>
    private static List<Listed> List(SqliteConnection connection, AgentDefinition agent, string path, List<string> unlisted)
    {
        var messages = SqliteHistory.MessageFingerprints(connection,
            "SELECT session_id,COUNT(*),MAX(seq),SUM(LENGTH(data)) FROM session_message WHERE type IN ('user','assistant') GROUP BY session_id");
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT id,directory,title,time_created,time_updated FROM session_v2 WHERE parent_id IS NULL ORDER BY time_created DESC";
        var listed = new List<Listed>();
        using var rows = query.ExecuteReader();
        while (rows.Read())
        {
            var id = rows.GetString(0);
            if (!AgentLaunchBuilder.IsExactSessionId("opencode", id)) { unlisted.Add(id); continue; }
            var directory = rows.IsDBNull(1) ? null : rows.GetString(1);
            // OpenCode stores folders with forward slashes ("C:/Users/…"); the catalog groups and resumes by Windows paths.
            if (directory is not null && Path.IsPathFullyQualified(directory)) directory = Path.GetFullPath(directory);
            var header = new Conversation(agent.Id, id, rows.IsDBNull(2) ? "" : rows.GetString(2), directory,
                DateTimeOffset.FromUnixTimeMilliseconds(rows.GetInt64(3)), DateTimeOffset.FromUnixTimeMilliseconds(rows.GetInt64(4)),
                path, agent.HistoryDirectory, "");
            listed.Add(new(header, string.Join('|', header.Title, header.WorkingDirectory, header.StartedAt.UtcTicks, header.UpdatedAt.UtcTicks, messages.GetValueOrDefault(id, ""))));
        }
        return listed;
    }

    /// <summary>The session with its messages read and its title resolved.</summary>
    private static Conversation Load(SqliteConnection connection, AgentDefinition agent, Conversation header)
    {
        using var messages = connection.CreateCommand();
        messages.CommandText = "SELECT type,data FROM session_message WHERE session_id=$id AND type IN ('user','assistant') ORDER BY seq";
        messages.Parameters.AddWithValue("$id", header.SessionId);
        var text = new List<string>();
        string? firstUser = null, firstMeaningful = null;
        using var reader = messages.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1)) continue;
            var user = reader.GetString(0) == "user";
            string content;
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(1));
                // A user message holds its text; an assistant message holds content blocks, of which only "text" is said
                // to the user ("reasoning" and tool calls are not).
                content = user ? TranscriptReader.Text(document.RootElement, "text") ?? ""
                    : TranscriptReader.ReadText(TranscriptReader.Property(document.RootElement, "content"));
            }
            catch (JsonException exception) { throw new InvalidDataException("An OpenCode message is not valid JSON; import was stopped instead of guessing.", exception); }
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (user) { firstUser ??= content; if (firstMeaningful is null && ConversationTitles.IsMeaningful(content)) firstMeaningful = content; }
            text.Add((user ? "You" : agent.Name) + "\n" + content);
        }
        return header with { Title = ConversationTitles.Resolve(header.Title, firstMeaningful ?? firstUser), Body = string.Join("\n\n", text) };
    }
}
