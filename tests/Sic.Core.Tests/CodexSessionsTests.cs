using System.Text.Json;
using Sic.Core.Agents;
using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class CodexSessionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "sic-codex-sessions-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _day;
    private readonly AgentDefinition _agent;
    private readonly string _folder;
    private readonly DateTimeOffset _launchedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

    public CodexSessionsTests()
    {
        _agent = new AgentDefinition("codex", "Codex", "codex", [], _root);
        _day = Path.Combine(_root, "sessions", DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"), DateTime.UtcNow.ToString("dd"));
        Directory.CreateDirectory(_day);
        _folder = Path.Combine(_root, "Project");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteRollout(string id, string? cwd, DateTimeOffset started, string? firstLine = null)
    {
        var path = Path.Combine(_day, "rollout-" + started.ToLocalTime().ToString("yyyy-MM-ddTHH-mm-ss") + "-" + id + ".jsonl");
        var header = firstLine ?? JsonSerializer.Serialize(new { timestamp = started.ToString("O"), type = "session_meta", payload = new { id, cwd, timestamp = started.ToString("O"), originator = "codex-tui" } });
        File.WriteAllText(path, header + "\n" + JsonSerializer.Serialize(new { type = "event_msg", payload = new { type = "task_started" } }) + "\n");
        return path;
    }

    [Fact]
    public void FindCreated_ReturnsOnlyTheSessionStartedInTheFolderSinceTheLaunch()
    {
        const string created = "01a0e8ef-ffe0-7ed3-a55a-ffe329f0634c";
        var path = WriteRollout(created, _folder.ToLowerInvariant() + Path.DirectorySeparatorChar, _launchedAt.AddSeconds(3));
        WriteRollout("01a0e568-fa5b-7ad3-ab67-d4a3384b0fba", _folder, _launchedAt.AddMinutes(-30));
        WriteRollout("01a0e64d-a94c-7653-83e6-9ed3cf261845", Path.Combine(_root, "Elsewhere"), _launchedAt.AddSeconds(4));
        WriteRollout("01a0c9eb-1a26-70e3-a72e-730e7f39eef8", null, _launchedAt.AddSeconds(5));
        WriteRollout("01a0cdff-9069-7e23-b367-6db64eb83eee", _folder, _launchedAt.AddSeconds(6), firstLine: "{\"type\":");
        WriteRollout("01a0aaaa-9069-7e23-b367-6db64eb83eee", _folder, _launchedAt.AddSeconds(7), firstLine: JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = "--last", cwd = _folder } }));
        var found = Assert.Single(CodexSessions.FindCreated(_agent, _folder, _launchedAt));
        Assert.Equal(created, found.SessionId);
        Assert.Equal(Path.GetFullPath(path), found.Path);
        Assert.Equal(_launchedAt.AddSeconds(3), found.StartedAt);
    }

    [Fact]
    public void FindCreated_ReportsEveryCandidateAndHonorsExclusions()
    {
        const string first = "01a0e8ef-ffe0-7ed3-a55a-ffe329f0634c", second = "01a0e568-fa5b-7ad3-ab67-d4a3384b0fba";
        WriteRollout(first, _folder, _launchedAt.AddSeconds(1));
        WriteRollout(second, _folder, _launchedAt.AddSeconds(2));
        Assert.Equal(2, CodexSessions.FindCreated(_agent, _folder, _launchedAt).Count);
        var remaining = Assert.Single(CodexSessions.FindCreated(_agent, _folder, _launchedAt, new HashSet<string>([first], StringComparer.OrdinalIgnoreCase)));
        Assert.Equal(second, remaining.SessionId);
        Assert.Empty(CodexSessions.FindCreated(_agent, _folder, _launchedAt, new HashSet<string>([first, second], StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void FindCreated_IsEmptyWithoutASessionsHomeAndNeverChangesASource()
    {
        Assert.Empty(CodexSessions.FindCreated(new AgentDefinition("codex", "Codex", "codex", [], Path.Combine(_root, "missing")), _folder, _launchedAt));
        var path = WriteRollout("01a0e8ef-ffe0-7ed3-a55a-ffe329f0634c", _folder, _launchedAt.AddSeconds(1));
        var original = File.ReadAllBytes(path);
        Assert.Single(CodexSessions.FindCreated(_agent, _folder, _launchedAt));
        Assert.Equal(original, File.ReadAllBytes(path));
    }
}
