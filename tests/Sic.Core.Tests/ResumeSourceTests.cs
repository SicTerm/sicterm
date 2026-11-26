using System.Text.Json;
using Sic.Core.Agents;
using Sic.Core.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ResumeSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "sic-resume-source-tests-" + Guid.NewGuid().ToString("N"));
    private const string Id = "01234567-89ab-4def-8123-456789abcdef";
    private const string OtherId = "abcdef01-2345-4678-9012-3456789abcde";
    private const string HermesId = "20260926_140330_dbff4d";

    public ResumeSourceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private AgentDefinition Claude => new("claude", "Claude Code", "claude", [], _root);
    private AgentDefinition Hermes => new("hermes", "Hermes", "hermes", [], _root);

    private string ClaudeTranscript(string name, string sessionId)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllLines(path, new object[]
        {
            new { type = "user", sessionId, cwd = _root, message = new { role = "user", content = "Resume me" } },
            new { type = "assistant", sessionId, message = new { role = "assistant", content = "Resumed" } }
        }.Select(row => JsonSerializer.Serialize(row)));
        return path;
    }

    private Conversation Entry(string agentId, string sessionId, string source) =>
        new(agentId, sessionId, "Saved", _root, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, source, _root, "", TextIndexed: false);

    [Fact]
    public void ATranscriptThatNamesTheSavedSessionIsReadyWithoutARefresh()
    {
        var path = ClaudeTranscript("ready.jsonl", Id);
        Assert.Equal(ResumeVerdict.Ready, ResumeSource.Check(Claude, Entry("claude", Id, path), Id));
    }

    [Fact]
    public void NoCatalogEntryOrAMissingSourceAsksForARefresh()
    {
        Assert.Equal(ResumeVerdict.SourceMissing, ResumeSource.Check(Claude, null, Id));
        Assert.Equal(ResumeVerdict.SourceMissing, ResumeSource.Check(Claude, Entry("claude", Id, Path.Combine(_root, "moved.jsonl")), Id));
    }

    [Fact]
    public void ATranscriptNamingAnotherSessionIsAnIdentityChange()
    {
        var path = ClaudeTranscript("other.jsonl", OtherId);
        Assert.Equal(ResumeVerdict.IdentityChanged, ResumeSource.Check(Claude, Entry("claude", Id, path), Id));
    }

    /// <summary>
    /// The check reads the transcript only as far as its first session ID, so it costs the same for a 35 MB transcript as for a
    /// small one; a later record naming another session is the import's to refuse, and such a file never reaches the catalog.
    /// </summary>
    [Fact]
    public void TheFirstIdentityInTheTranscriptDecidesWithoutReadingTheRest()
    {
        var path = ClaudeTranscript("conflict.jsonl", Id);
        File.AppendAllText(path, JsonSerializer.Serialize(new { type = "user", sessionId = OtherId, message = new { role = "user", content = "x" } }) + "\n");
        Assert.Equal(ResumeVerdict.Ready, ResumeSource.Check(Claude, Entry("claude", Id, path), Id));
        Assert.Throws<InvalidDataException>(() => TranscriptReader.ReadMetadata(Claude, path));
        var unreadable = Path.Combine(_root, "unreadable.jsonl");
        File.WriteAllText(unreadable, "{broken\n" + JsonSerializer.Serialize(new { type = "user", sessionId = Id }) + "\n");
        Assert.ThrowsAny<JsonException>(() => ResumeSource.Check(Claude, Entry("claude", Id, unreadable), Id));
    }

    [Fact]
    public void AHermesSessionIsFoundByItsRowAndOnlyAmongTerminalSessions()
    {
        var path = Path.Combine(_root, "state.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sessions(id TEXT,source TEXT,title TEXT,cwd TEXT,started_at REAL);
                CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT);
                INSERT INTO sessions VALUES('20260926_140330_dbff4d','cli','Terminal session',NULL,1800000000);
                INSERT INTO sessions VALUES('20260926_150330_abcd12','signal','Gateway session',NULL,1800000000);
                """;
            command.ExecuteNonQuery();
        }
        var before = File.ReadAllBytes(path);
        Assert.Equal(ResumeVerdict.Ready, ResumeSource.Check(Hermes, Entry("hermes", HermesId, path), HermesId));
        Assert.Equal(ResumeVerdict.SessionMissing, ResumeSource.Check(Hermes, Entry("hermes", "20260926_150330_abcd12", path), "20260926_150330_abcd12"));
        Assert.Equal(ResumeVerdict.SessionMissing, ResumeSource.Check(Hermes, Entry("hermes", "20260926_160330_ffff00", path), "20260926_160330_ffff00"));
        // The catalog holds no session that has no message yet; its row is enough.
        Assert.Equal(ResumeVerdict.Ready, ResumeSource.Check(Hermes, null, HermesId));
        Assert.Equal(ResumeVerdict.SessionMissing, ResumeSource.Check(Hermes with { HistoryDirectory = Path.Combine(_root, "none") }, null, HermesId));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void EveryVerdictButReadyDescribesAnErrorThatStartsNothing()
    {
        Assert.Equal("The saved conversation was not found. No new conversation was started.", ResumeSource.Describe(ResumeVerdict.SourceMissing));
        Assert.Equal("The session no longer exists in the agent's history.", ResumeSource.Describe(ResumeVerdict.SessionMissing));
        Assert.Equal("The saved conversation's file now holds a different session.", ResumeSource.Describe(ResumeVerdict.IdentityChanged));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResumeSource.Describe(ResumeVerdict.Ready));
    }
}
