using Sic.Core.Agents;

namespace Sic.Core.Sessions;

public static class TranscriptReader
{
    /// <summary>Longest first-message excerpt kept when a transcript's full text is not imported.</summary>
    public const int ExcerptLength = 2000;

    /// <summary>Reads identity, folder, title, timing and every user/assistant message of a Claude Code or Codex transcript.</summary>
    public static Conversation ReadJsonLines(AgentDefinition agent, string path) => Read(agent, path, includeText: true, note: null);

    /// <summary>
    /// Streams the transcript for identity, folder, title, timing and a bounded first-message excerpt without holding the
    /// message text in memory. Applies the same identity checks as <see cref="ReadJsonLines"/>. The result is flagged
    /// <see cref="Conversation.TextIndexed"/> = false and carries <paramref name="note"/> for the interface.
    /// </summary>
    public static Conversation ReadMetadata(AgentDefinition agent, string path, string? note = null) => Read(agent, path, includeText: false, note);

    /// <summary>
    /// The transcript's own session ID, read from the first record that carries one (Claude Code writes it on every record,
    /// Codex on its first, the session_meta) and no further: what an exact resume checks the saved ID against. On a 35 MB
    /// transcript this is a fraction of a millisecond where <see cref="ReadMetadata"/>, which scans every record, took 270 ms
    /// (measured October 16, 2026). A record that is not valid JSON, or a transcript with no usable ID before its end, is
    /// refused as the import refuses it; the import's scan of the whole file still catches a later record naming another session.
    /// </summary>
    public static string ReadSessionId(AgentDefinition agent, string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = ParseLine(line, reader.EndOfStream);
            if (document is null) break;
            var row = document.RootElement;
            if (Property(row, "isSidechain").ValueKind == System.Text.Json.JsonValueKind.True ||
                Property(row, "isMeta").ValueKind == System.Text.Json.JsonValueKind.True) continue;
            string? id = null;
            if (agent.Id == "claude") id = Text(row, "sessionId");
            else if (agent.Id == "codex" && Text(row, "type") == "session_meta")
            {
                var payload = Property(row, "payload");
                id = Text(payload, "id") ?? Text(payload, "session_id");
            }
            if (id is null) continue;
            if (!AgentLaunchBuilder.IsExactSessionId(agent.Id, id)) break;
            return id;
        }
        throw new InvalidDataException("The transcript has no supported authoritative conversation ID.");
    }

    private static Conversation Read(AgentDefinition agent, string path, bool includeText, string? note)
    {
        string? id = null, directory = null, title = null, firstUser = null, firstMeaningful = null;
        DateTimeOffset? started = null, updated = null;
        var messages = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = ParseLine(line, reader.EndOfStream);
            if (document is null) break;
            var row = document.RootElement;
            if (Property(row, "isSidechain").ValueKind == System.Text.Json.JsonValueKind.True ||
                Property(row, "isMeta").ValueKind == System.Text.Json.JsonValueKind.True) continue;
            var type = Text(row, "type");
            var time = Text(row, "timestamp");
            if (DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var instant))
            {
                if (started is null || instant < started) started = instant;
                if (updated is null || instant > updated) updated = instant;
            }
            System.Text.Json.JsonElement message = default;
            if (agent.Id == "claude")
            {
                var candidateId = Text(row, "sessionId");
                if (candidateId is not null && id is not null && candidateId != id)
                    throw new InvalidDataException("The transcript contains conflicting conversation IDs.");
                id ??= candidateId;
                directory = Text(row, "cwd") ?? directory;
                if (type == "ai-title") title = Text(row, "aiTitle") ?? title;
                if (type == "custom-title") title = Text(row, "customTitle") ?? title;
                if (type is "user" or "assistant") message = Property(row, "message");
            }
            else if (agent.Id == "codex")
            {
                var payload = Property(row, "payload");
                if (type == "session_meta")
                {
                    var candidateId = Text(payload, "id") ?? Text(payload, "session_id");
                    if (id is not null && candidateId != id)
                        throw new InvalidDataException("The transcript contains conflicting conversation IDs.");
                    id = candidateId;
                    directory = Text(payload, "cwd");
                }
                if (type == "response_item" && Text(payload, "type") == "message") message = payload;
            }
            var role = Text(message, "role");
            if (role is not ("user" or "assistant")) continue;
            var text = ReadText(Property(message, "content"));
            if (string.IsNullOrWhiteSpace(text)) continue;
            // Codex's preamble (its environment block, the folder's AGENTS.md) is a user message in the rollout but not a request: it neither titles nor opens the excerpt.
            if (role == "user" && !ConversationTitles.IsAgentPreamble(text))
            {
                firstUser ??= text;
                // Slash-command records and tool XML start with '<' or '/'; the excerpt and the title should show the first real request.
                if (firstMeaningful is null && ConversationTitles.IsMeaningful(text)) firstMeaningful = text.TrimStart();
            }
            if (includeText) messages.Add((role == "user" ? "You" : agent.Name) + "\n" + text);
        }
        if (id is null || !AgentLaunchBuilder.IsExactSessionId(agent.Id, id))
            throw new InvalidDataException("The transcript has no supported authoritative conversation ID.");
        // A transcript that opens with /clear is titled after the request that follows it, not "<command-name>/clear</command-name>".
        title = ConversationTitles.Resolve(title, firstMeaningful ?? firstUser);
        var fallback = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        var body = includeText ? string.Join("\n\n", messages)
            : (firstMeaningful ?? firstUser) is { } first ? "You\n" + (first.Length > ExcerptLength ? first[..ExcerptLength] + "…" : first) : "";
        return new Conversation(agent.Id, id, title, directory, started ?? fallback, updated ?? fallback,
            Path.GetFullPath(path), agent.HistoryDirectory, body, TextIndexed: includeText, TextNote: includeText ? null : note);
    }

    private static System.Text.Json.JsonDocument? ParseLine(string line, bool atEnd)
    {
        try { return System.Text.Json.JsonDocument.Parse(line); }
        catch (System.Text.Json.JsonException) when (atEnd) { return null; }
    }

    internal static System.Text.Json.JsonElement Property(System.Text.Json.JsonElement value, string key) =>
        value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty(key, out var found) ? found : default;

    internal static string? Text(System.Text.Json.JsonElement value, string key)
    {
        var found = Property(value, key);
        return found.ValueKind == System.Text.Json.JsonValueKind.String ? found.GetString() : null;
    }

    internal static string ReadText(System.Text.Json.JsonElement content)
    {
        if (content.ValueKind == System.Text.Json.JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != System.Text.Json.JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray()
            .Where(block => Text(block, "type") is "text" or "input_text" or "output_text")
            .Select(block => Text(block, "text") ?? ""));
    }
}
