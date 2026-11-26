using Sic.Core.Agents;
using Sic.Core.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sic.Core.Tests;

public sealed class OpenCodeHistoryReaderTests : IDisposable
{
    private const string Session = "ses_ef18561f8ffeGZqmbqg6IknrnW";
    private const string Untitled = "ses_ef18561f8ff0AAAAbbbbCCCCdd";
    private const string Subagent = "ses_ef18561f8fe0ZZZZyyyyXXXXww";
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "sic-opencode-tests-" + Guid.NewGuid().ToString("N"), "opencode");
    public OpenCodeHistoryReaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_root)!, true);

    private AgentDefinition Agent => new("opencode", "OpenCode", "opencode", [], _root);
    private string Database => Path.Combine(_root, "opencode.db");

    // The columns SIC reads, plus enough of OpenCode 2.0.23's others that the reader is seen to ignore them.
    private void Create(string extra = "")
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE session_v2(id TEXT PRIMARY KEY,project_id TEXT,parent_id TEXT,slug TEXT,directory TEXT NOT NULL,title TEXT,
              version TEXT,time_created INTEGER NOT NULL,time_updated INTEGER NOT NULL,time_archived INTEGER);
            CREATE TABLE session_message(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,type TEXT NOT NULL,seq INTEGER NOT NULL,
              time_created INTEGER,time_updated INTEGER,data TEXT NOT NULL);
            INSERT INTO session_v2 VALUES($session,'p',NULL,'mighty-circuit','C:/Work/cobalt','Friendly greeting','2.0.23',1791244279311,1791244281533,NULL);
            INSERT INTO session_v2 VALUES($untitled,'p',NULL,'cosmic-pixel','C:/Work/cobalt',NULL,'2.0.23',1791244279000,1791244279100,NULL);
            INSERT INTO session_v2 VALUES($subagent,'p',$session,'sub','C:/Work/cobalt','Subagent task','2.0.23',1791244280000,1791244280100,NULL);
            INSERT INTO session_message VALUES('m1',$session,'user',4,0,0,'{"time":{"created":1},"text":"Find the cobalt lighthouse","files":[]}');
            INSERT INTO session_message VALUES('m2',$session,'assistant',5,0,0,'{"agent":"build","content":[{"type":"reasoning","text":"PRIVATE-REASONING"},{"type":"text","text":"The lighthouse is ready."}],"finish":"stop"}');
            INSERT INTO session_message VALUES('m3',$session,'idle',10,0,0,'{"outcome":"succeeded"}');
            INSERT INTO session_message VALUES('m4',$untitled,'user',4,0,0,'{"text":"First line of the ask\nsecond line"}');
            INSERT INTO session_message VALUES('m5',$subagent,'user',4,0,0,'{"text":"SUBAGENT-PROMPT"}');
            """ + extra;
        command.Parameters.AddWithValue("$session", Session);
        command.Parameters.AddWithValue("$untitled", Untitled);
        command.Parameters.AddWithValue("$subagent", Subagent);
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Read_ImportsTopLevelSessionsWithWhatWasSaidAndLeavesTheSourceUnchanged()
    {
        Create();
        var before = File.ReadAllBytes(Database);
        var sessions = OpenCodeHistoryReader.Read(Agent);
        Assert.Equal([Session, Untitled], sessions.Select(session => session.SessionId));
        var result = sessions[0];
        Assert.Equal("Friendly greeting", result.Title);
        Assert.Equal(@"C:\Work\cobalt", result.WorkingDirectory);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1791244279311), result.StartedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1791244281533), result.UpdatedAt);
        Assert.Equal(Database, result.SourcePath);
        Assert.Equal("You\nFind the cobalt lighthouse\n\nOpenCode\nThe lighthouse is ready.", result.Body);
        Assert.Equal("First line of the ask", sessions[1].Title);
        Assert.DoesNotContain(sessions, session => session.Body.Contains("SUBAGENT-PROMPT") || session.Body.Contains("PRIVATE-REASONING"));
        Assert.Equal(before, File.ReadAllBytes(Database));
    }

    [Fact]
    public void Exists_FindsOnlyATopLevelSession()
    {
        Create();
        Assert.True(OpenCodeHistoryReader.Exists(Agent, Session));
        Assert.False(OpenCodeHistoryReader.Exists(Agent, Subagent));
        Assert.False(OpenCodeHistoryReader.Exists(Agent, "ses_ef18561f8ffeAAAAAAAAAAAAAA"));
        Assert.False(OpenCodeHistoryReader.Exists(Agent with { HistoryDirectory = Path.Combine(_root, "missing") }, Session));
    }

    [Fact]
    public void Provider_IsTheSessionModelsProviderOrNone()
    {
        Create("""
            ALTER TABLE session_v2 ADD COLUMN model TEXT;
            UPDATE session_v2 SET model='{"id":"gpt-5.6-sol-fast","providerID":"openai","variant":"high"}' WHERE id=$session;
            UPDATE session_v2 SET model='not json' WHERE id=$untitled;
            """);
        Assert.Equal("openai", OpenCodeHistoryReader.Provider(Agent, Session));
        Assert.Null(OpenCodeHistoryReader.Provider(Agent, Untitled));
        Assert.Null(OpenCodeHistoryReader.Provider(Agent, Subagent));
        Assert.Null(OpenCodeHistoryReader.Provider(Agent, "ses_ef18561f8ffeAAAAAAAAAAAAAA"));
        Assert.Null(OpenCodeHistoryReader.Provider(Agent with { HistoryDirectory = Path.Combine(_root, "missing") }, Session));
    }

    [Fact]
    public void Provider_IsNoneWithoutAModelColumn()
    {
        Create();
        Assert.Null(OpenCodeHistoryReader.Provider(Agent, Session));
    }

    [Fact]
    public void Read_LeavesOutAnIdItCannotResumeAndStopsAtASchemaItDoesNotKnow()
    {
        Create("INSERT INTO session_v2 VALUES('ses_custom','p',NULL,'x','C:/Work',NULL,'2.0.23',1,1,NULL);");
        var read = OpenCodeHistoryReader.Read(Agent, _ => null);
        Assert.Equal(2, read.Imported.Count);
        Assert.Equal("ses_custom", Assert.Single(read.Unlisted!));
        File.Delete(Database);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE session(id TEXT, directory TEXT)";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => OpenCodeHistoryReader.Read(Agent));
    }

    [Fact]
    public void Refresh_ImportsTheDatabaseOnceUntilItChanges()
    {
        Create();
        var catalog = new ConversationCatalog(Path.Combine(Path.GetDirectoryName(_root)!, "catalog.db"));
        var indexer = new HistoryIndexer(catalog);
        AgentDefinition[] agents = [Agent];
        Assert.Equal(2, indexer.Refresh(agents).Imported);
        Assert.Single(catalog.Search("lighthouse"));
        Assert.Equal(1, indexer.Refresh(agents).Unchanged);
        Assert.Equal(ResumeVerdict.Ready, ResumeSource.Check(Agent, catalog.Search("lighthouse")[0], Session));
        // A reply added to the untitled session: that session is read again, the other is left unread.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO session_message VALUES('m6',$untitled,'assistant',5,0,0,'{\"content\":[{\"type\":\"text\",\"text\":\"The sapphire lantern.\"}]}'); UPDATE session_v2 SET time_updated=1791244279200 WHERE id=$untitled;";
            command.Parameters.AddWithValue("$untitled", Untitled);
            command.ExecuteNonQuery();
        }
        File.SetLastWriteTimeUtc(Database, File.GetLastWriteTimeUtc(Database).AddSeconds(2));
        var changed = indexer.Refresh(agents);
        Assert.Equal((1, 1), (changed.Imported, changed.Unchanged));
        Assert.Equal(Untitled, Assert.Single(catalog.Search("sapphire")).SessionId);
        Assert.Single(catalog.Search("lighthouse"));
    }
}
