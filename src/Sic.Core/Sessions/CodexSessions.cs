using System.Globalization;
using System.Text.Json;
using Sic.Core.Agents;

namespace Sic.Core.Sessions;

/// <summary>A Codex rollout as its first line identifies it: the session ID, the folder the session runs in, and when it started.</summary>
public sealed record CodexRollout(string SessionId, string Path, string? WorkingDirectory, DateTimeOffset StartedAt);

/// <summary>
/// Finds the rollout Codex writes for a new interactive session. Nothing in the launch names the session: Codex creates
/// <c>sessions/YYYY/MM/DD/rollout-&lt;local time&gt;-&lt;id&gt;.jsonl</c> when the first message is sent, not when its interface
/// starts, with a <c>session_meta</c> record on the first line. A pane started without an ID learns it from that file: the
/// rollout of a session that started in the pane's folder at or after the launch.
/// </summary>
public static class CodexSessions
{
    /// <summary>Every rollout started in <paramref name="directory"/> at or after <paramref name="launchedAt"/> whose ID is not in <paramref name="exclude"/>.</summary>
    public static IReadOnlyList<CodexRollout> FindCreated(AgentDefinition agent, string directory, DateTimeOffset launchedAt, IReadOnlySet<string>? exclude = null)
    {
        var root = System.IO.Path.Combine(agent.HistoryDirectory, "sessions");
        if (!Directory.Exists(root)) return [];
        var found = new List<CodexRollout>();
        foreach (var folder in DayFolders(root, launchedAt))
        {
            // The listing carries each file's write time, so no file is stat'ed on its own (the poll runs every two seconds).
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("rollout-*.jsonl"))
            {
                // A rollout not written since the launch cannot be the new session's; this keeps the poll from reading every header.
                if (file.LastWriteTimeUtc < launchedAt.UtcDateTime.AddMinutes(-1)) continue;
                var rollout = ReadHeader(agent, file.FullName);
                if (rollout is null || rollout.StartedAt < launchedAt.AddSeconds(-2) || rollout.WorkingDirectory is null) continue;
                if (!SameFolder(rollout.WorkingDirectory, directory) || exclude?.Contains(rollout.SessionId) == true) continue;
                found.Add(rollout);
            }
        }
        return found;
    }

    /// <summary>The session_meta record on a rollout's first line, or null when the file does not start with a usable one.</summary>
    public static CodexRollout? ReadHeader(AgentDefinition agent, string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null && string.IsNullOrWhiteSpace(line)) { }
            if (line is null) return null;
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            if (TranscriptReader.Text(row, "type") != "session_meta") return null;
            var payload = TranscriptReader.Property(row, "payload");
            var id = TranscriptReader.Text(payload, "id") ?? TranscriptReader.Text(payload, "session_id");
            if (id is null || !AgentLaunchBuilder.IsExactSessionId(agent.Id, id)) return null;
            if (!DateTimeOffset.TryParse(TranscriptReader.Text(payload, "timestamp") ?? TranscriptReader.Text(row, "timestamp"),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var started))
                started = new DateTimeOffset(File.GetCreationTimeUtc(path));
            return new CodexRollout(id, System.IO.Path.GetFullPath(path), TranscriptReader.Text(payload, "cwd"), started);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Codex names the day folder by date; both the local and the UTC dates from the launch until now are tried.</summary>
    private static IEnumerable<string> DayFolders(string root, DateTimeOffset launchedAt)
    {
        var now = DateTimeOffset.UtcNow;
        var first = new[] { launchedAt.UtcDateTime.Date, launchedAt.LocalDateTime.Date }.Min();
        var last = new[] { now.UtcDateTime.Date, now.LocalDateTime.Date }.Max();
        for (var day = first; day <= last && day < first.AddDays(31); day = day.AddDays(1))
        {
            var folder = System.IO.Path.Combine(root, day.ToString("yyyy", CultureInfo.InvariantCulture), day.ToString("MM", CultureInfo.InvariantCulture), day.ToString("dd", CultureInfo.InvariantCulture));
            if (Directory.Exists(folder)) yield return folder;
        }
    }

    private static bool SameFolder(string first, string second) => string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        try { return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
