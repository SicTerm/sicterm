using System.Text.Json;
using Sic.Core.Agents;
using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TranscriptReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "command-central-history-tests-" + Guid.NewGuid().ToString("N"));
    private const string Id = "01234567-89ab-4def-8123-456789abcdef";

    public TranscriptReaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReadJsonLines_UsesSavedTitleAndExcludesInternalMetadataMessages()
    {
        var path = Path.Combine(_root, "metadata.jsonl");
        File.WriteAllLines(path, new object[]
        {
            new { type = "user", sessionId = Id, cwd = _root, isMeta = true, message = new { role = "user", content = "INTERNAL-ONLY" } },
            new { type = "user", sessionId = Id, cwd = _root, message = new { role = "user", content = "Actual question" } },
            new { type = "ai-title", sessionId = Id, aiTitle = "Saved conversation title" }
        }.Select(row => JsonSerializer.Serialize(row)));
        var result = TranscriptReader.ReadJsonLines(new("claude", "Claude", "claude", [], _root), path);
        Assert.Equal("Saved conversation title", result.Title);
        Assert.DoesNotContain("INTERNAL-ONLY", result.Body);
        Assert.True(result.TextIndexed);
        Assert.Null(result.TextNote);
    }

    [Fact]
    public void ReadJsonLines_IgnoresAnIncompleteTrailingWriteButRejectsConflictingIdentity()
    {
        var path = Path.Combine(_root, "partial.jsonl");
        var header = JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = Id, cwd = _root } });
        File.WriteAllText(path, header + "\n{\"type\":");
        var agent = new AgentDefinition("codex", "Codex", "codex", [], _root);
        Assert.Equal(Id, TranscriptReader.ReadJsonLines(agent, path).SessionId);
        File.AppendAllText(path, "\"session_meta\",\"payload\":{\"id\":\"abcdef01-2345-4678-9012-3456789abcde\"}}\n");
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadJsonLines(agent, path));
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadMetadata(agent, path));
    }

    [Fact]
    public void ReadJsonLines_RejectsAnUntrustedIdentityAndNeverInventsAFolder()
    {
        var path = Path.Combine(_root, "identity.jsonl");
        var agent = new AgentDefinition("codex", "Codex", "codex", [], _root);
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = "--last" } }));
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadJsonLines(agent, path));
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadMetadata(agent, path));
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = Id } }));
        Assert.Null(TranscriptReader.ReadJsonLines(agent, path).WorkingDirectory);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void ReadJsonLines_PreservesAuthoritativeIdentityFolderAndText(string agentId)
    {
        var path = Path.Combine(_root, "conversation.jsonl");
        object[] rows = agentId == "claude"
            ? [new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:30:00Z", message = new { role = "user", content = "Find the cobalt lighthouse" } },
                new { type = "assistant", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:31:00Z", message = new { role = "assistant", content = new[] { new { type = "text", text = "The lighthouse is ready." } } } }]
            : [new { type = "session_meta", timestamp = "2026-09-26T16:30:00Z", payload = new { id = Id, cwd = _root, timestamp = "2026-09-26T16:30:00Z" } },
                new { type = "response_item", timestamp = "2026-09-26T16:30:01Z", payload = new { type = "message", role = "user", content = new[] { new { type = "input_text", text = "Find the cobalt lighthouse" } } } },
                new { type = "response_item", timestamp = "2026-09-26T16:31:00Z", payload = new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "The lighthouse is ready." } } } }];
        File.WriteAllLines(path, rows.Select(row => JsonSerializer.Serialize(row)));
        var original = File.ReadAllBytes(path);
        var result = TranscriptReader.ReadJsonLines(new AgentDefinition(agentId, agentId, agentId, [], _root), path);
        Assert.Equal(Id, result.SessionId);
        Assert.Equal(_root, result.WorkingDirectory);
        Assert.Equal("Find the cobalt lighthouse", result.Title);
        Assert.Contains("The lighthouse is ready.", result.Body);
        Assert.Equal(DateTimeOffset.Parse("2026-09-26T16:30:00Z"), result.StartedAt);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadJsonLines_SkipsCodexsAgentsPreambleForTheTitleAndTheExcerpt()
    {
        // Codex opens a rollout with its environment block and the folder's AGENTS.md as user messages, before the first request.
        var path = Path.Combine(_root, "preamble.jsonl");
        object[] rows =
        [
            new { type = "session_meta", timestamp = "2026-10-20T16:30:00Z", payload = new { id = Id, cwd = _root } },
            new { type = "response_item", timestamp = "2026-10-20T16:30:01Z", payload = new { type = "message", role = "user", content = new[] { new { type = "input_text", text = "<environment_context>\n  <cwd>" + _root + "</cwd>\n</environment_context>" } } } },
            new { type = "response_item", timestamp = "2026-10-20T16:30:01Z", payload = new { type = "message", role = "user", content = new[] { new { type = "input_text", text = "# AGENTS.md instructions for " + _root + "\n\n<INSTRUCTIONS>\nAlways tidy up.\n</INSTRUCTIONS>" } } } },
            new { type = "response_item", timestamp = "2026-10-20T16:30:02Z", payload = new { type = "message", role = "user", content = new[] { new { type = "input_text", text = "Find the cobalt lighthouse" } } } }
        ];
        File.WriteAllLines(path, rows.Select(row => JsonSerializer.Serialize(row)));
        var agent = new AgentDefinition("codex", "Codex", "codex", [], _root);
        Assert.Equal("Find the cobalt lighthouse", TranscriptReader.ReadJsonLines(agent, path).Title);
        var metadata = TranscriptReader.ReadMetadata(agent, path);
        Assert.Equal("Find the cobalt lighthouse", metadata.Title);
        Assert.StartsWith("You\nFind the cobalt lighthouse", metadata.Body);
        // A rollout holding the preamble alone has no request to be titled after.
        File.WriteAllLines(path, rows.Take(3).Select(row => JsonSerializer.Serialize(row)));
        Assert.Equal("Untitled conversation", TranscriptReader.ReadJsonLines(agent, path).Title);
    }

    [Fact]
    public void ReadSessionId_StopsAtTheFirstRecordThatNamesTheSessionAndSkipsWhatTheImportSkips()
    {
        var path = Path.Combine(_root, "identity-first.jsonl");
        var claude = new AgentDefinition("claude", "Claude", "claude", [], _root);
        File.WriteAllLines(path, new object[]
        {
            new { type = "summary", summary = "A continued session" },
            new { type = "user", sessionId = "ffffffff-ffff-4fff-8fff-ffffffffffff", isSidechain = true, message = new { role = "user", content = "a subagent's record" } },
            new { type = "user", sessionId = Id, cwd = _root, message = new { role = "user", content = "Actual question" } },
            new { type = "assistant", sessionId = "abcdef01-2345-4678-9012-3456789abcde", message = new { role = "assistant", content = "a later record naming another session" } }
        }.Select(row => JsonSerializer.Serialize(row)));
        Assert.Equal(Id, TranscriptReader.ReadSessionId(claude, path));
        // The import's scan still refuses the conflict the fast read does not look for.
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadMetadata(claude, path));
        var codex = new AgentDefinition("codex", "Codex", "codex", [], _root);
        var rollout = Path.Combine(_root, "rollout.jsonl");
        File.WriteAllText(rollout, JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = Id, cwd = _root } }) + "\n{\"type\":");
        Assert.Equal(Id, TranscriptReader.ReadSessionId(codex, rollout));
        File.WriteAllText(rollout, JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = "--last" } }));
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadSessionId(codex, rollout));
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "summary", summary = "nothing else" }) + "\n");
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadSessionId(claude, path));
        File.WriteAllText(path, "{broken\n" + JsonSerializer.Serialize(new { type = "user", sessionId = Id }) + "\n");
        Assert.ThrowsAny<JsonException>(() => TranscriptReader.ReadSessionId(claude, path));
    }

    [Fact]
    public void ReadMetadata_KeepsIdentityTimingAndABoundedExcerptWithoutTheFullText()
    {
        var path = Path.Combine(_root, "large.jsonl");
        var padding = new string('x', 50_000);
        File.WriteAllLines(path, new object[]
        {
            new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:30:00Z", message = new { role = "user", content = "<command-name>/clear</command-name>" } },
            new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:31:00Z", message = new { role = "user", content = "Find the cobalt lighthouse " + padding } },
            new { type = "assistant", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:32:00Z", message = new { role = "assistant", content = "The lighthouse is ready." } },
            new { type = "custom-title", sessionId = Id, customTitle = "Lighthouse work" }
        }.Select(row => JsonSerializer.Serialize(row)));
        var agent = new AgentDefinition("claude", "Claude", "claude", [], _root);
        var full = TranscriptReader.ReadJsonLines(agent, path);
        var metadata = TranscriptReader.ReadMetadata(agent, path, "too large");
        Assert.Equal((full.SessionId, full.WorkingDirectory, full.Title, full.StartedAt, full.UpdatedAt),
            (metadata.SessionId, metadata.WorkingDirectory, metadata.Title, metadata.StartedAt, metadata.UpdatedAt));
        Assert.Equal("Lighthouse work", metadata.Title);
        Assert.True(full.TextIndexed);
        Assert.False(metadata.TextIndexed);
        Assert.Equal("too large", metadata.TextNote);
        Assert.StartsWith("You\nFind the cobalt lighthouse", metadata.Body);
        Assert.True(metadata.Body.Length <= TranscriptReader.ExcerptLength + 8, "the excerpt must stay bounded");
        Assert.DoesNotContain("The lighthouse is ready.", metadata.Body);
        Assert.DoesNotContain("<command-name>", metadata.Body);
        Assert.Contains("The lighthouse is ready.", full.Body);
    }

    [Fact]
    public void ATranscriptThatOpensWithACommandIsTitledAfterTheFirstRealRequest()
    {
        var path = Path.Combine(_root, "cleared.jsonl");
        File.WriteAllLines(path, new object[]
        {
            new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:30:00Z", message = new { role = "user", content = "<command-name>/clear</command-name>\n<command-message>clear</command-message>" } },
            new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:31:00Z", message = new { role = "user", content = "Sessions pane grouping and layout\nSecond line." } },
            new { type = "assistant", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:32:00Z", message = new { role = "assistant", content = "Looking." } }
        }.Select(row => JsonSerializer.Serialize(row)));
        var agent = new AgentDefinition("claude", "Claude", "claude", [], _root);
        Assert.Equal("Sessions pane grouping and layout", TranscriptReader.ReadJsonLines(agent, path).Title);
        Assert.Equal("Sessions pane grouping and layout", TranscriptReader.ReadMetadata(agent, path, "too large").Title);
        // A transcript of commands alone is titled after the command itself rather than its record or nothing.
        var only = Path.Combine(_root, "only-commands.jsonl");
        File.WriteAllLines(only, new object[]
        {
            new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:30:00Z", message = new { role = "user", content = "<command-name>/clear</command-name>\n<command-message>clear</command-message>" } },
            new { type = "user", sessionId = Id, cwd = _root, timestamp = "2026-09-26T16:31:00Z", message = new { role = "user", content = "/help" } }
        }.Select(row => JsonSerializer.Serialize(row)));
        Assert.Equal("/clear", TranscriptReader.ReadJsonLines(agent, only).Title);
        Assert.Equal("/help", ConversationTitles.Resolve(null, "/help\nmore"));
        Assert.False(ConversationTitles.IsMeaningful("  <local-command-stdout>x</local-command-stdout>"));
        Assert.True(ConversationTitles.IsMeaningful("Find the lighthouse"));
    }
}
