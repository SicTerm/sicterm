using Sic.Core.Agents;
using Sic.Core.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sic.Core.Tests;

public sealed class HermesHistoryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "command-central-hermes-tests-" + Guid.NewGuid().ToString("N"));
    public HermesHistoryReaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Read_ImportsTerminalTextWithoutChangingSourceOrReadingGatewayMessages()
    {
        var path = Path.Combine(_root, "state.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sessions(id TEXT,source TEXT,title TEXT,cwd TEXT,started_at REAL,last_activity_at REAL);
                CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT,timestamp REAL,active INTEGER);
                INSERT INTO sessions VALUES('20260926_140330_dbff4d','cli','Read-only import',$cwd,1800000000,1800000100);
                INSERT INTO sessions VALUES('20260926_150330_abcd12','signal','Excluded gateway',$cwd,1800000000,1800000100);
                INSERT INTO messages VALUES(1,'20260926_140330_dbff4d','user','Find the cobalt lighthouse',1800000000,1);
                INSERT INTO messages VALUES(2,'20260926_140330_dbff4d','assistant','The lighthouse is ready.',1800000100,1);
                INSERT INTO messages VALUES(3,'20260926_140330_dbff4d','tool','TOOL-PRIVATE',1800000100,1);
                INSERT INTO messages VALUES(4,'20260926_140330_dbff4d','user','REWOUND-MESSAGE',1800000100,0);
                """;
            command.Parameters.AddWithValue("$cwd", _root);
            command.ExecuteNonQuery();
        }
        var before = File.ReadAllBytes(path);
        var result = Assert.Single(HermesHistoryReader.Read(new AgentDefinition("hermes", "Hermes", "hermes", [], _root)));
        Assert.Equal("20260926_140330_dbff4d", result.SessionId);
        Assert.Equal(_root, result.WorkingDirectory);
        Assert.Equal("Read-only import", result.Title);
        Assert.Contains("The lighthouse is ready.", result.Body);
        Assert.DoesNotContain("TOOL-PRIVATE", result.Body);
        Assert.DoesNotContain("REWOUND-MESSAGE", result.Body);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Refresh_ReadsOnlyTheSessionsThatChangedSinceTheLastImportAndFindsOneByItsTitle()
    {
        var path = Path.Combine(_root, "state.db");
        void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$cwd", _root);
            command.ExecuteNonQuery();
        }
        Execute("""
            CREATE TABLE sessions(id TEXT,source TEXT,title TEXT,cwd TEXT,started_at REAL,last_activity_at REAL);
            CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT,timestamp REAL,active INTEGER);
            INSERT INTO sessions VALUES('20260926_140330_dbff4d','cli','Blue Maple migration',$cwd,1800000000,1800000100);
            INSERT INTO sessions VALUES('20260926_150330_abcd12','tui',NULL,$cwd,1800000200,1800000300);
            INSERT INTO messages VALUES(1,'20260926_140330_dbff4d','user','Find the cobalt lighthouse',1800000000,1);
            INSERT INTO messages VALUES(2,'20260926_150330_abcd12','user','Amber beacon question',1800000200,1);
            """);
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], _root);
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var indexer = new HistoryIndexer(catalog);
        var first = indexer.Refresh([agent]);
        Assert.Equal((2, 0), (first.Imported, first.Unchanged));
        // The database untouched: nothing in it is read.
        var second = indexer.Refresh([agent]);
        Assert.Equal((0, 1), (second.Imported, second.Unchanged));
        // A message added to one session: that session is read again, the other is not.
        Execute("INSERT INTO messages VALUES(3,'20260926_150330_abcd12','assistant','The sapphire lantern.',1800000400,1); UPDATE sessions SET last_activity_at=1800000400 WHERE id='20260926_150330_abcd12';");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        var third = indexer.Refresh([agent]);
        Assert.Equal((1, 1), (third.Imported, third.Unchanged));
        Assert.Single(catalog.Search("sapphire"));
        Assert.Single(catalog.Search("cobalt"));
        // A message changed in place, nothing else: still found.
        Execute("UPDATE messages SET content='Find the cobalt lighthouse again' WHERE id=1;");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(4));
        var fourth = indexer.Refresh([agent]);
        Assert.Equal((1, 1), (fourth.Imported, fourth.Unchanged));
        Assert.Contains("again", catalog.Get("hermes:20260926_140330_dbff4d")!.Body);
        // A new launch keeps the per-session fingerprints with the catalog.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(6));
        var fresh = new HistoryIndexer(new ConversationCatalog(Path.Combine(_root, "catalog.db"))).Refresh([agent]);
        Assert.Equal((0, 2), (fresh.Imported, fresh.Unchanged));
        // By title: the named session, and the untitled one only when its first line is the name.
        Assert.Equal("20260926_140330_dbff4d", Assert.Single(HermesHistoryReader.ReadByTitle(agent, "Blue Maple migration")).SessionId);
        Assert.Equal("20260926_150330_abcd12", Assert.Single(HermesHistoryReader.ReadByTitle(agent, "Amber beacon question")).SessionId);
        Assert.Empty(HermesHistoryReader.ReadByTitle(agent, "No such name"));
    }

    [Fact]
    public void Refresh_LeavesOutOnlyASessionWithAnIdItCannotResumeAndReadsTheLocalIds()
    {
        var path = Path.Combine(_root, "state.db");
        const string local = "local-c62ff67c35f5cc1acf536a397da28962d78481645d09354f64d8dcb7e6964dcc";
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE sessions(id TEXT,source TEXT,title TEXT,cwd TEXT,started_at REAL,last_activity_at REAL);
                CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT,timestamp REAL,active INTEGER);
                INSERT INTO sessions VALUES('20260926_140330_dbff4d','cli','Timestamped',$cwd,1800000000,1800000100);
                INSERT INTO sessions VALUES('{local}','cli','SIC-0123456789ab4def8123456789abcdef',$cwd,1800000200,1800000300);
                INSERT INTO sessions VALUES('future:shape','cli','Unknown shape',$cwd,1800000400,1800000500);
                INSERT INTO messages VALUES(1,'{local}','user','The violet harbor',1800000200,1);
                INSERT INTO messages VALUES(2,'20260926_140330_dbff4d','user','The amber pier',1800000100,1);
                """;
            command.Parameters.AddWithValue("$cwd", _root);
            command.ExecuteNonQuery();
        }
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], _root);
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var result = new HistoryIndexer(catalog).Refresh([agent]);
        Assert.Equal((2, 0), (result.Imported, result.Failed));
        var (unlistedAgent, unlistedId) = Assert.Single(result.Unlisted!);
        Assert.Equal(("hermes", "future:shape"), (unlistedAgent.Id, unlistedId));
        Assert.Single(catalog.Search("violet"));
        // A new session's binding finds a local id by its creation name, past the session it cannot read.
        Assert.Equal(local, Assert.Single(HermesHistoryReader.ReadByTitle(agent, "SIC-0123456789ab4def8123456789abcdef")).SessionId);
    }

    [Fact]
    public void Read_KeepsASessionTheGatewayRewroteToSourceLocalAndLeavesOutAnEditorsAndAChats()
    {
        var path = Path.Combine(_root, "state.db");
        const string turned = "local-1111111111111111111111111111111111111111111111111111111111111111";
        const string editor = "local-2222222222222222222222222222222222222222222222222222222222222222";
        const string unknown = "local-3333333333333333333333333333333333333333333333333333333333333333";
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE sessions(id TEXT,source TEXT,created_source TEXT,title TEXT,cwd TEXT,started_at REAL,last_activity_at REAL);
                CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT,timestamp REAL,active INTEGER);
                INSERT INTO sessions VALUES('{turned}','local','cli','Reply with only OK',$cwd,1800000000,1800000100);
                INSERT INTO sessions VALUES('{editor}','local','acp','From an editor',$cwd,1800000200,1800000300);
                INSERT INTO sessions VALUES('{unknown}','local',NULL,'Before the column',$cwd,1800000400,1800000500);
                INSERT INTO sessions VALUES('20260926_140330_dbff4d','telegram','telegram','A chat',$cwd,1800000600,1800000700);
                INSERT INTO sessions VALUES('20260926_150330_abcd12','cli','cli','Not yet turned',$cwd,1800000800,1800000900);
                INSERT INTO messages VALUES(1,'{turned}','user','Reply with only the word OK',1800000000,1);
                INSERT INTO messages VALUES(2,'{unknown}','user','Before the column',1800000400,1);
                INSERT INTO messages VALUES(3,'20260926_150330_abcd12','user','Not yet turned',1800000800,1);
                """;
            command.Parameters.AddWithValue("$cwd", _root);
            command.ExecuteNonQuery();
        }
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], _root);
        // Newest first: the session still marked cli, the one from before created_source was filled, and the one turned local.
        Assert.Equal(["20260926_150330_abcd12", unknown, turned], HermesHistoryReader.Read(agent).Select(conversation => conversation.SessionId).ToArray());
        Assert.True(HermesHistoryReader.Exists(agent, turned));
        Assert.False(HermesHistoryReader.Exists(agent, editor));
        Assert.False(HermesHistoryReader.Exists(agent, "20260926_140330_dbff4d"));
    }

    [Fact]
    public void Read_LeavesOutASessionWithNoMessageYetWhichTheBindingAndAResumeStillFind()
    {
        var path = Path.Combine(_root, "state.db");
        const string empty = "local-7777777777777777777777777777777777777777777777777777777777777777";
        const string spoken = "local-8888888888888888888888888888888888888888888888888888888888888888";
        void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$cwd", _root);
            command.ExecuteNonQuery();
        }
        // Hermes writes a session's row when it begins, untitled, and deletes it if it ends with no messages.
        Execute($"""
            CREATE TABLE sessions(id TEXT,source TEXT,created_source TEXT,title TEXT,cwd TEXT,started_at REAL,last_activity_at REAL);
            CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT,timestamp REAL,active INTEGER);
            INSERT INTO sessions VALUES('{empty}','tui','tui',NULL,$cwd,1800000100,NULL);
            INSERT INTO sessions VALUES('{spoken}','local','cli','Harbor lights',$cwd,1800000000,1800000050);
            INSERT INTO messages VALUES(1,'{spoken}','user','Where are the harbor lights?',1800000010,1);
            """);
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], _root);
        Assert.Equal([spoken], HermesHistoryReader.Read(agent).Select(conversation => conversation.SessionId).ToArray());
        Assert.True(HermesHistoryReader.Exists(agent, empty));
        Assert.Equal(empty, Assert.Single(HermesHistoryReader.FindCreated(agent, _root, DateTimeOffset.FromUnixTimeSeconds(1800000090))).SessionId);
        // Imported once its first request is written.
        var indexer = new HistoryIndexer(new ConversationCatalog(Path.Combine(_root, "catalog.db")));
        Assert.Equal(1, indexer.Refresh([agent]).Imported);
        Execute($"INSERT INTO messages VALUES(2,'{empty}','user','Name the lighthouse',1800000200,1);");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        var second = indexer.Refresh([agent]);
        Assert.Equal((1, 1), (second.Imported, second.Unchanged));
    }

    [Fact]
    public void FindCreated_FindsTheSessionBegunInThePanesFolderAfterItsLaunchAndBeforeTheNextOne()
    {
        var path = Path.Combine(_root, "state.db");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        string Id(char digit) => "local-" + new string(digit, 64);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // The pane launched at 1800000100. Hermes's TUI writes "tui"; its command line resolves the folder, here in another case
            // and with a trailing separator.
            command.CommandText = $"""
                CREATE TABLE sessions(id TEXT,source TEXT,created_source TEXT,title TEXT,cwd TEXT,started_at REAL,last_activity_at REAL);
                CREATE TABLE messages(id INTEGER,session_id TEXT,role TEXT,content TEXT,timestamp REAL,active INTEGER);
                INSERT INTO sessions VALUES('{Id('1')}','local','cli',NULL,$folder,1800000050,1800000060);
                INSERT INTO sessions VALUES('{Id('2')}','tui','tui',NULL,$shouted,1800000108.5,NULL);
                INSERT INTO sessions VALUES('{Id('3')}','cli','cli',NULL,$other,1800000109,NULL);
                INSERT INTO sessions VALUES('{Id('4')}','local','acp',NULL,$folder,1800000110,NULL);
                INSERT INTO sessions VALUES('{Id('5')}','cli','cli',NULL,NULL,1800000111,NULL);
                INSERT INTO sessions VALUES('{Id('6')}','cli','cli',NULL,$folder,1800000130,NULL);
                INSERT INTO messages VALUES(1,'{Id('2')}','user','Fix the login form',1800000140,1);
                """;
            command.Parameters.AddWithValue("$folder", folder);
            command.Parameters.AddWithValue("$shouted", folder.ToUpperInvariant() + Path.DirectorySeparatorChar);
            command.Parameters.AddWithValue("$other", other);
            command.ExecuteNonQuery();
        }
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], _root);
        var launched = DateTimeOffset.FromUnixTimeSeconds(1800000100);
        // Not the session from before the launch, another folder's, an editor's, one with no folder, or the next launch's.
        var created = Assert.Single(HermesHistoryReader.FindCreated(agent, folder, launched, launched.AddSeconds(20)));
        Assert.Equal(Id('2'), created.SessionId);
        // Untitled until Hermes names it: called after its first request meanwhile, with its folder and messages read.
        Assert.Equal("Fix the login form", created.Title);
        Assert.Contains("Fix the login form", created.Body);
        // Without the next launch's bound, newest first; the pane's folder may be written another way too.
        Assert.Equal([Id('6'), Id('2')], HermesHistoryReader.FindCreated(agent, folder + Path.DirectorySeparatorChar, launched).Select(found => found.SessionId).ToArray());
        Assert.Empty(HermesHistoryReader.FindCreated(agent, other, launched.AddSeconds(10)));
        Assert.Empty(HermesHistoryReader.FindCreated(agent with { HistoryDirectory = Path.Combine(_root, "none") }, folder, launched));
    }
}
