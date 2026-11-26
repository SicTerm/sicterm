using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Sic.Core.Sessions;

/// <summary>The first characters of a stored transcript (<see cref="ConversationCatalog.GetBodyStart"/>), and whether the transcript goes on past them.</summary>
public sealed record BodyStart(string Text, bool Truncated);

/// <summary>Rebuildable local index. Never writes to an agent's authoritative history.</summary>
public sealed class ConversationCatalog
{
    /// <summary>
    /// The schema this build writes (PRAGMA user_version). 0 was the first catalog: an FTS table of its own content keyed by
    /// the conversation's text key, which FTS5 cannot index, so taking a conversation's search row out scanned the whole table.
    /// 1 (October 10, 2026) keys each conversation by an integer id, keeps the search index as an external-content FTS table on
    /// the conversations table kept by triggers, and adds the sources table of import fingerprints.
    /// </summary>
    private const int SchemaVersion = 1;

    public string DatabasePath { get; }

    public ConversationCatalog(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = Open();
        Execute(connection, "PRAGMA journal_mode=WAL;");
        using var transaction = connection.BeginTransaction();
        var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));
        if (version < 1) MigrateToVersion1(connection);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS conversations (
                id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, agent TEXT NOT NULL, title TEXT NOT NULL,
                folder TEXT NOT NULL, updated INTEGER NOT NULL, data TEXT NOT NULL, body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS conversations_updated ON conversations(updated DESC, key);
            CREATE INDEX IF NOT EXISTS conversations_agent_updated ON conversations(agent, updated DESC, key);
            CREATE VIRTUAL TABLE IF NOT EXISTS conversation_search USING fts5(title, body, folder, content='conversations', content_rowid='id');
            CREATE TRIGGER IF NOT EXISTS conversations_search_insert AFTER INSERT ON conversations BEGIN
                INSERT INTO conversation_search(rowid,title,body,folder) VALUES (new.id,new.title,new.body,new.folder);
            END;
            CREATE TRIGGER IF NOT EXISTS conversations_search_delete AFTER DELETE ON conversations BEGIN
                INSERT INTO conversation_search(conversation_search,rowid,title,body,folder) VALUES ('delete',old.id,old.title,old.body,old.folder);
            END;
            CREATE TRIGGER IF NOT EXISTS conversations_search_update AFTER UPDATE ON conversations BEGIN
                INSERT INTO conversation_search(conversation_search,rowid,title,body,folder) VALUES ('delete',old.id,old.title,old.body,old.folder);
                INSERT INTO conversation_search(rowid,title,body,folder) VALUES (new.id,new.title,new.body,new.folder);
            END;
            CREATE TABLE IF NOT EXISTS sources (source TEXT PRIMARY KEY, fingerprint TEXT NOT NULL);
            """);
        if (version < 1)
        {
            // The rows an older catalog kept are indexed again under the new table: one pass over every body, once.
            Execute(connection, "INSERT INTO conversation_search(conversation_search) VALUES ('rebuild');");
        }
        if (version < SchemaVersion) Execute(connection, "PRAGMA user_version=" + SchemaVersion + ";");
        transaction.Commit();
    }

    /// <summary>
    /// A version-0 catalog's conversations table is rebuilt with an integer id and a folder column, and its search table dropped
    /// for the constructor to make anew and fill. A catalog that has no conversations table yet needs nothing.
    /// </summary>
    private static void MigrateToVersion1(SqliteConnection connection)
    {
        Execute(connection, "DROP TABLE IF EXISTS conversation_search;");
        if (Convert.ToInt32(Scalar(connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='conversations';")) == 0) return;
        Execute(connection, """
            CREATE TABLE conversations_v1 (
                id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, agent TEXT NOT NULL, title TEXT NOT NULL,
                folder TEXT NOT NULL, updated INTEGER NOT NULL, data TEXT NOT NULL, body TEXT NOT NULL);
            INSERT INTO conversations_v1(key,agent,title,folder,updated,data,body)
                SELECT key,agent,title,coalesce(json_extract(data,'$.WorkingDirectory'),''),updated,data,body FROM conversations ORDER BY updated,key;
            DROP TABLE conversations;
            ALTER TABLE conversations_v1 RENAME TO conversations;
            """);
    }

    /// <summary>Writes one conversation in a transaction of its own. A run of them goes through <see cref="BeginBatch"/>.</summary>
    public void Upsert(Conversation conversation)
    {
        using var batch = BeginBatch();
        batch.Upsert(conversation);
        batch.Commit();
    }

    /// <summary>
    /// One connection and one transaction for a run of writes, with its statements prepared once. <see cref="Batch.Commit"/>
    /// makes the writes so far durable and starts the next transaction; disposing the batch drops any writes after the last commit.
    /// </summary>
    public Batch BeginBatch() => new(Open());

    /// <summary>Every import fingerprint the catalog holds, by source; see <see cref="Batch.SetFingerprint"/>.</summary>
    public IReadOnlyDictionary<string, string> Fingerprints()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source,fingerprint FROM sources;";
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public sealed class Batch : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteCommand _upsert;
        private readonly SqliteCommand _fingerprint;
        private SqliteTransaction _transaction;

        internal Batch(SqliteConnection connection)
        {
            _connection = connection;
            _transaction = connection.BeginTransaction();
            _upsert = connection.CreateCommand();
            // A row whose stored record and text already match is left alone: its search entries stay, and the update trigger,
            // which would index the body again, does not run.
            _upsert.CommandText = """
                INSERT INTO conversations(key,agent,title,folder,updated,data,body) VALUES($key,$agent,$title,$folder,$updated,$data,$body)
                ON CONFLICT(key) DO UPDATE SET agent=excluded.agent,title=excluded.title,folder=excluded.folder,updated=excluded.updated,data=excluded.data,body=excluded.body
                WHERE data IS NOT excluded.data OR body IS NOT excluded.body;
                """;
            foreach (var name in new[] { "$key", "$agent", "$title", "$folder", "$updated", "$data", "$body" }) _upsert.Parameters.Add(name, name == "$updated" ? SqliteType.Integer : SqliteType.Text);
            _fingerprint = connection.CreateCommand();
            _fingerprint.CommandText = "INSERT INTO sources(source,fingerprint) VALUES($source,$fingerprint) ON CONFLICT(source) DO UPDATE SET fingerprint=excluded.fingerprint;";
            _fingerprint.Parameters.Add("$source", SqliteType.Text);
            _fingerprint.Parameters.Add("$fingerprint", SqliteType.Text);
            _upsert.Transaction = _fingerprint.Transaction = _transaction;
        }

        public void Upsert(Conversation conversation)
        {
            _upsert.Parameters["$key"].Value = conversation.Key;
            _upsert.Parameters["$agent"].Value = conversation.AgentId;
            _upsert.Parameters["$title"].Value = conversation.Title;
            _upsert.Parameters["$folder"].Value = conversation.WorkingDirectory ?? "";
            _upsert.Parameters["$updated"].Value = conversation.UpdatedAt.UtcTicks;
            _upsert.Parameters["$data"].Value = JsonSerializer.Serialize(conversation with { Body = "" });
            _upsert.Parameters["$body"].Value = conversation.Body;
            _upsert.ExecuteNonQuery();
        }

        /// <summary>Records what an import source looked like when its conversations were last written, so a later import can leave it alone.</summary>
        public void SetFingerprint(string source, string fingerprint)
        {
            _fingerprint.Parameters["$source"].Value = source;
            _fingerprint.Parameters["$fingerprint"].Value = fingerprint;
            _fingerprint.ExecuteNonQuery();
        }

        public void Commit()
        {
            _transaction.Commit();
            _transaction.Dispose();
            _transaction = _connection.BeginTransaction();
            _upsert.Transaction = _fingerprint.Transaction = _transaction;
        }

        public void Dispose()
        {
            _upsert.Dispose();
            _fingerprint.Dispose();
            _transaction.Dispose();
            _connection.Dispose();
        }
    }

    /// <summary>
    /// The newest conversations matching <paramref name="query"/>, of <paramref name="agentId"/> or of every agent but those in
    /// <paramref name="hiddenAgents"/> (agents switched off in Settings): left out in the query, so they take no place in the
    /// <paramref name="limit"/>.
    /// </summary>
    public IReadOnlyList<Conversation> Search(string query, string? agentId = null, int limit = 500,
        IReadOnlyDictionary<string, string>? aliases = null, string? folderScope = null, IReadOnlyCollection<string>? hiddenAgents = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 5000);
        if (query.Length > 4096) throw new ArgumentException("Search text is too long.", nameof(query));
        var tokens = Regex.Matches(query, @"[\p{L}\p{N}_]+").Select(match => match.Value).ToArray();
        var words = tokens.Select(word => "\"" + word + "\"*").ToArray();
        var useSearch = words.Length != 0;
        var scope = NormalizeScope(folderScope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.data FROM conversations c " +
            (useSearch ? "JOIN conversation_search f ON f.rowid=c.id WHERE conversation_search MATCH $query " : "WHERE 1=1 ") +
            "AND ($agent IS NULL OR c.agent=$agent) AND c.agent NOT IN (SELECT value FROM json_each($hidden)) " +
            (scope is null ? "" : "AND substr(replace(c.folder,'/','\\') || '\\', 1, $scopeLength) = $scope COLLATE NOCASE ") +
            "ORDER BY c.updated DESC,c.key LIMIT $limit;";
        if (useSearch) command.Parameters.AddWithValue("$query", string.Join(" AND ", words));
        command.Parameters.AddWithValue("$agent", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$hidden", JsonSerializer.Serialize(hiddenAgents ?? []));
        if (scope is not null) { command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$scopeLength", scope.Length); }
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<Conversation>();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<Conversation>(reader.GetString(0))!;
            if (InScope(item, scope)) result.Add(item);
        }
        if (aliases is null || aliases.Count == 0) return result;
        var keys = result.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var aliasMatches = new HashSet<string>(StringComparer.Ordinal);
        if (tokens.Length != 0)
        {
            // The names the words match, then their records in one query on the open connection: a record per matching
            // name used to open a connection of its own, and a short query matches most names.
            var named = new List<string>();
            foreach (var (key, name) in aliases)
            {
                var nameTokens = Regex.Matches(name, @"[\p{L}\p{N}_]+").Select(match => match.Value).ToArray();
                if (tokens.All(token => nameTokens.Any(word => word.StartsWith(token, StringComparison.OrdinalIgnoreCase)))) named.Add(key);
            }
            var records = Records(connection, named, agentId: null);
            foreach (var key in named)
            {
                if (!records.TryGetValue(key, out var item) || (agentId is not null && item.AgentId != agentId) || hiddenAgents?.Contains(item.AgentId) == true || !InScope(item, scope)) continue;
                aliasMatches.Add(key);
                if (keys.Add(key)) result.Add(item);
                if (aliasMatches.Count == limit) break;
            }
        }
        return result.OrderByDescending(item => aliasMatches.Contains(item.Key))
            .ThenByDescending(item => item.UpdatedAt).ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(item => aliases.TryGetValue(item.Key, out var name) && !string.IsNullOrWhiteSpace(name)
                ? item with { Title = name } : item).ToList();
    }

    /// <summary>
    /// The listed conversations that pass the same agent filter, hidden agents and folder scope as <see cref="Search"/>, without their bodies,
    /// with local names applied. The list asks for pinned conversations this way, because a pin older than the newest
    /// <see cref="Search"/> page would otherwise drop out of the Pinned group. Keys not in the catalog are skipped.
    /// </summary>
    public IReadOnlyList<Conversation> Find(IEnumerable<string> keys, string? agentId = null, string? folderScope = null,
        IReadOnlyDictionary<string, string>? aliases = null, IReadOnlyCollection<string>? hiddenAgents = null)
    {
        var scope = NormalizeScope(folderScope);
        using var connection = Open();
        var wanted = keys.Distinct(StringComparer.Ordinal).ToList();
        var records = Records(connection, wanted, agentId);
        var result = new List<Conversation>();
        foreach (var key in wanted)
        {
            if (!records.TryGetValue(key, out var item) || hiddenAgents?.Contains(item.AgentId) == true || !InScope(item, scope)) continue;
            result.Add(aliases is not null && aliases.TryGetValue(item.Key, out var name) && !string.IsNullOrWhiteSpace(name) ? item with { Title = name } : item);
        }
        return result;
    }

    /// <summary>The records of <paramref name="keys"/> that the catalog holds, of <paramref name="agentId"/> or of any agent, in one query; a key not held is absent.</summary>
    private static Dictionary<string, Conversation> Records(SqliteConnection connection, IReadOnlyCollection<string> keys, string? agentId)
    {
        var records = new Dictionary<string, Conversation>(StringComparer.Ordinal);
        if (keys.Count == 0) return records;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM conversations WHERE key IN (SELECT value FROM json_each($keys)) AND ($agent IS NULL OR agent=$agent);";
        command.Parameters.AddWithValue("$keys", JsonSerializer.Serialize(keys));
        command.Parameters.AddWithValue("$agent", (object?)agentId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<Conversation>(reader.GetString(0))!;
            records[item.Key] = item;
        }
        return records;
    }

    /// <summary>How many conversations the catalog holds, the hidden agents' left out: what the welcome card says of the PC.</summary>
    public int Count(IReadOnlyCollection<string>? hiddenAgents = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM conversations WHERE agent NOT IN (SELECT value FROM json_each($hidden));";
        command.Parameters.AddWithValue("$hidden", JsonSerializer.Serialize(hiddenAgents ?? []));
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The working folder and last update of every conversation of any agent updated since <paramref name="since"/> and with a
    /// folder recorded: what <see cref="State.AutoPins.Rank"/> counts. Read from the indexed columns alone, so no body is loaded.
    /// </summary>
    public IReadOnlyList<(string? Folder, DateTimeOffset UpdatedAt)> FolderActivity(DateTimeOffset since)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT folder,updated FROM conversations WHERE updated >= $since AND folder <> '';";
        command.Parameters.AddWithValue("$since", since.UtcTicks);
        using var reader = command.ExecuteReader();
        var result = new List<(string?, DateTimeOffset)>();
        while (reader.Read()) result.Add((reader.GetString(0), new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero)));
        return result;
    }

    /// <summary>The conversation with its whole stored transcript, which can run to tens of megabytes; <see cref="GetHeader"/> when the text is not needed.</summary>
    public Conversation? Get(string key) => Read(key, withBody: true);

    /// <summary>The conversation's record alone (identity, folder, title, timing, source), with an empty <see cref="Conversation.Body"/>.</summary>
    public Conversation? GetHeader(string key) => Read(key, withBody: false);

    /// <summary>
    /// The start of the stored transcript: its first <paramref name="maxChars"/> characters, cut in SQLite so the rest never
    /// leaves the database, and whether anything was left out. For the placeholder page and the preview card, which show a
    /// bounded portion and used to read the whole body for it. Null when the key is not in the catalog.
    /// </summary>
    public BodyStart? GetBodyStart(string key, int maxChars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT substr(body,1,$max), length(body) > $max FROM conversations WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$max", maxChars);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new BodyStart(reader.GetString(0), reader.GetInt64(1) != 0) : null;
    }

    private Conversation? Read(string key, bool withBody)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = withBody ? "SELECT data,body FROM conversations WHERE key=$key;" : "SELECT data FROM conversations WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var item = JsonSerializer.Deserialize<Conversation>(reader.GetString(0))!;
        return withBody ? item with { Body = reader.GetString(1) } : item;
    }

    /// <summary>Folder scope with one trailing separator so "repos\web" cannot match "repos\web-api".</summary>
    public static string? NormalizeScope(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var trimmed = folder.Trim().Replace('/', '\\').TrimEnd('\\');
        return trimmed.Length == 0 ? null : trimmed + '\\';
    }

    /// <summary>Re-checks the SQL prefix test with full Unicode case folding; the SQL side is ASCII-only NOCASE.</summary>
    private static bool InScope(Conversation item, string? scope) =>
        scope is null || (item.WorkingDirectory is { Length: > 0 } folder
            && (folder.Replace('/', '\\').TrimEnd('\\') + '\\').StartsWith(scope, StringComparison.OrdinalIgnoreCase));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }
}
