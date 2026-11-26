using Microsoft.Data.Sqlite;
using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ConversationCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "command-central-catalog-tests-" + Guid.NewGuid().ToString("N"));
    public ConversationCatalogTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Upsert_CreatesADurableSearchableLocalIndex()
    {
        var path = Path.Combine(_root, "catalog.db");
        var item = new Conversation("claude", "01234567-89ab-4def-8123-456789abcdef", "A useful session", _root,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, "original.jsonl"), _root, "The cobalt lighthouse is ready.");
        new ConversationCatalog(path).Upsert(item);
        var reopened = new ConversationCatalog(path);
        Assert.Equal(item.Key, Assert.Single(reopened.Search("cobalt lighthouse")).Key);
        Assert.Equal(item, reopened.Get(item.Key));
        Assert.Empty(reopened.Search("absentword"));
        Assert.Single(reopened.Search(""));
    }

    [Fact]
    public void Search_AndFind_LeaveOutHiddenAgentsWithoutTheirTakingPlacesInTheLimit()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var now = DateTimeOffset.UtcNow;
        Conversation Item(string agent, string id, int minutes) => new(agent, id, agent + " cobalt " + id, _root, now, now.AddMinutes(minutes), Path.Combine(_root, id), _root, "cobalt");
        catalog.Upsert(Item("claude", "01234567-89ab-4def-8123-456789abcdef", 1));
        catalog.Upsert(Item("opencode", "ses_ef18561f8ffeGZqmbqg6IknrnW", 2));
        catalog.Upsert(Item("opencode", "ses_ef18561f8ff0AAAAbbbbCCCCdd", 3));
        string[] hidden = ["opencode"];
        Assert.Equal("claude", Assert.Single(catalog.Search("", limit: 1, hiddenAgents: hidden)).AgentId);
        Assert.Equal("claude", Assert.Single(catalog.Search("cobalt", hiddenAgents: hidden)).AgentId);
        Assert.Equal(3, catalog.Search("cobalt").Count);
        Assert.Empty(catalog.Search("cobalt", agentId: "opencode", hiddenAgents: hidden));
        // A local name that matches is no way round the switch.
        var aliases = new Dictionary<string, string> { ["opencode:ses_ef18561f8ffeGZqmbqg6IknrnW"] = "Lighthouse plan" };
        Assert.Empty(catalog.Search("lighthouse", aliases: aliases, hiddenAgents: hidden));
        Assert.Single(catalog.Search("lighthouse", aliases: aliases));
        string[] keys = ["claude:01234567-89ab-4def-8123-456789abcdef", "opencode:ses_ef18561f8ffeGZqmbqg6IknrnW"];
        Assert.Equal("claude", Assert.Single(catalog.Find(keys, hiddenAgents: hidden)).AgentId);
        Assert.Equal(2, catalog.Find(keys).Count);
        Assert.NotNull(catalog.Get("opencode:ses_ef18561f8ffeGZqmbqg6IknrnW"));
    }

    [Fact]
    public void Count_LeavesOutHiddenAgents()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var now = DateTimeOffset.UtcNow;
        Conversation Item(string agent, string id) => new(agent, id, agent + " " + id, _root, now, now, Path.Combine(_root, id), _root, "body");
        Assert.Equal(0, catalog.Count());
        catalog.Upsert(Item("claude", "01234567-89ab-4def-8123-456789abcdef"));
        catalog.Upsert(Item("opencode", "ses_ef18561f8ffeGZqmbqg6IknrnW"));
        catalog.Upsert(Item("opencode", "ses_ef18561f8ff0AAAAbbbbCCCCdd"));
        Assert.Equal(3, catalog.Count());
        Assert.Equal(1, catalog.Count(["opencode"]));
        Assert.Equal(0, catalog.Count(["opencode", "claude"]));
    }

    [Fact]
    public void Upsert_RoundTripsTheTextIndexedFlagAndReplacesStaleBodyText()
    {
        var path = Path.Combine(_root, "catalog.db");
        var catalog = new ConversationCatalog(path);
        var full = new Conversation("claude", "01234567-89ab-4def-8123-456789abcdef", "Growing session", _root,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, "growing.jsonl"), _root, "You\nfirst ask\n\nClaude\nstale answer text");
        catalog.Upsert(full);
        Assert.Single(catalog.Search("stale answer"));
        var partial = full with { Body = "You\nfirst ask", TextIndexed = false, TextNote = "Transcript is 67.8 MiB; text not indexed.", UpdatedAt = full.UpdatedAt.AddMinutes(5) };
        catalog.Upsert(partial);
        var reopened = new ConversationCatalog(path);
        Assert.Equal(partial, reopened.Get(partial.Key));
        Assert.False(Assert.Single(reopened.Search("")).TextIndexed);
        Assert.Empty(reopened.Search("stale answer"));
        Assert.Single(reopened.Search("first ask"));
    }

    [Fact]
    public void Search_FindsLocalNameAfterHistoryReimportWithoutChangingAuthoritativeTitle()
    {
        var path = Path.Combine(_root, "catalog.db");
        var source = Path.Combine(_root, "source.jsonl");
        var item = new Conversation("claude", "01234567-89ab-4def-8123-456789abcdef", "Original history title", _root,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, source, _root, "Original body.");
        var catalog = new ConversationCatalog(path);
        catalog.Upsert(item);
        var names = new Dictionary<string, string> { [item.Key] = "Blue Maple migration" };
        Assert.Equal("Blue Maple migration", Assert.Single(catalog.Search("maple migr", aliases: names)).Title);
        catalog.Upsert(item with { UpdatedAt = item.UpdatedAt.AddMinutes(1) });
        var reopened = new ConversationCatalog(path);
        Assert.Equal("Blue Maple migration", Assert.Single(reopened.Search("blue map", aliases: names)).Title);
        Assert.Equal("Blue Maple migration", Assert.Single(reopened.Search("", aliases: names)).Title);
        var newer = item with { SessionId = "ec293be8-e3af-4365-8862-633825531a60", Title = "Maple road", UpdatedAt = item.UpdatedAt.AddHours(1) };
        reopened.Upsert(newer);
        Assert.Equal(item.Key, Assert.Single(reopened.Search("MAPLE", limit: 1, aliases: names)).Key);
        Assert.Empty(reopened.Search("blue map", agentId: "codex", aliases: names));
        Assert.Equal("Original history title", reopened.Get(item.Key)!.Title);
    }

    [Fact]
    public void Search_ScopesToAFolderAndItsSubfoldersCaseInsensitively()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        Conversation Make(string id, string folder) => new("claude", id, "Title " + id, folder,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, id + ".jsonl"), _root, "body " + id);
        var alpha = Make("11111111-1111-4111-8111-111111111111", @"C:\batch\repos\alpha");
        var alphabet = Make("22222222-2222-4222-8222-222222222222", @"c:\Batch\Repos\alphabet\src");
        var other = Make("33333333-3333-4333-8333-333333333333", @"D:\other");
        foreach (var item in new[] { alpha, alphabet, other }) catalog.Upsert(item);
        Assert.Equal([alpha.Key], catalog.Search("", folderScope: @"c:\BATCH\repos\alpha\").Select(item => item.Key));
        Assert.Equal(2, catalog.Search("", folderScope: @"C:\batch\repos").Count);
        Assert.Equal(3, catalog.Search("", folderScope: "  ").Count);
        Assert.Equal([alphabet.Key], catalog.Search("body", folderScope: "C:/Batch/Repos/alphabet").Select(item => item.Key));
        var names = new Dictionary<string, string> { [other.Key] = "Blue Maple" };
        Assert.Empty(catalog.Search("maple", aliases: names, folderScope: @"C:\batch"));
        Assert.Single(catalog.Search("maple", aliases: names, folderScope: @"D:\"));
        Assert.Equal(@"C:\batch\repos\", ConversationCatalog.NormalizeScope("C:/batch/repos/"));
        Assert.Null(ConversationCatalog.NormalizeScope(null));
    }

    [Fact]
    public void Find_ReturnsPinnedConversationsBeyondTheSearchPageWithFiltersAndNames()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var start = DateTimeOffset.UtcNow.AddDays(-30);
        Conversation Item(int index, string agent, string folder) => new(agent, Guid.NewGuid().ToString(), "Session " + index, folder,
            start.AddMinutes(index), start.AddMinutes(index), Path.Combine(_root, index + ".jsonl"), _root, "Body text " + index);
        var oldest = Item(0, "claude", Path.Combine(_root, "repo"));
        var otherAgent = Item(1, "codex", Path.Combine(_root, "repo"));
        var otherFolder = Item(2, "claude", Path.Combine(_root, "repo-two"));
        catalog.Upsert(oldest); catalog.Upsert(otherAgent); catalog.Upsert(otherFolder);
        for (var index = 3; index < 8; index++) catalog.Upsert(Item(index, "claude", Path.Combine(_root, "repo")));
        Assert.DoesNotContain(catalog.Search("", limit: 3), item => item.Key == oldest.Key);

        var names = new Dictionary<string, string> { [oldest.Key] = "Pinned by name" };
        var found = catalog.Find([oldest.Key, otherAgent.Key, otherFolder.Key, "claude:missing", oldest.Key], aliases: names);
        Assert.Equal([oldest.Key, otherAgent.Key, otherFolder.Key], found.Select(item => item.Key));
        Assert.Equal("Pinned by name", found[0].Title);
        Assert.All(found, item => Assert.Equal("", item.Body));
        Assert.Equal([oldest.Key, otherFolder.Key], catalog.Find([oldest.Key, otherAgent.Key, otherFolder.Key], agentId: "claude").Select(item => item.Key));
        Assert.Equal([oldest.Key, otherAgent.Key], catalog.Find([oldest.Key, otherAgent.Key, otherFolder.Key], folderScope: Path.Combine(_root, "repo")).Select(item => item.Key));
    }

    [Fact]
    public void Batch_KeepsWritesUpToTheLastCommitAndTheFingerprintsWithThem()
    {
        var path = Path.Combine(_root, "catalog.db");
        var catalog = new ConversationCatalog(path);
        Conversation Make(string id, string body) => new("claude", id, "Title " + id, _root,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, id + ".jsonl"), _root, body);
        var kept = Make("11111111-1111-4111-8111-111111111111", "cobalt lighthouse");
        var alsoKept = Make("22222222-2222-4222-8222-222222222222", "amber beacon");
        var dropped = Make("33333333-3333-4333-8333-333333333333", "sapphire lantern");
        using (var batch = catalog.BeginBatch())
        {
            batch.Upsert(kept);
            batch.SetFingerprint("claude|one", "1:10:20");
            batch.Commit();
            batch.Upsert(alsoKept);
            batch.SetFingerprint("claude|two", "1:30:40");
            batch.Commit();
            batch.Upsert(dropped);
            batch.SetFingerprint("claude|three", "1:50:60");
        }
        var reopened = new ConversationCatalog(path);
        Assert.Equal(new[] { kept.Key, alsoKept.Key }.Order(StringComparer.Ordinal), reopened.Search("").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Single(reopened.Search("cobalt"));
        Assert.Empty(reopened.Search("sapphire"));
        Assert.Equal(new Dictionary<string, string> { ["claude|one"] = "1:10:20", ["claude|two"] = "1:30:40" }, reopened.Fingerprints());
        using (var batch = reopened.BeginBatch())
        {
            batch.SetFingerprint("claude|one", "1:11:21");
            batch.Commit();
        }
        Assert.Equal("1:11:21", reopened.Fingerprints()["claude|one"]);
    }

    [Fact]
    public void Upsert_ReplacesTheSearchEntriesOfAChangedConversationAndLeavesAnUnchangedOneAlone()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var item = new Conversation("claude", "01234567-89ab-4def-8123-456789abcdef", "Cobalt title", @"C:\repos\alpha",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, "source.jsonl"), _root, "The cobalt lighthouse.");
        catalog.Upsert(item);
        catalog.Upsert(item);
        Assert.Single(catalog.Search("cobalt"));
        Assert.Single(catalog.Search("lighthouse", folderScope: @"C:\repos"));
        var changed = item with { Title = "Amber title", WorkingDirectory = @"D:\other\beta", Body = "The amber beacon." };
        catalog.Upsert(changed);
        Assert.Empty(catalog.Search("cobalt"));
        Assert.Empty(catalog.Search("lighthouse"));
        Assert.Empty(catalog.Search("alpha"));
        Assert.Equal(changed.Key, Assert.Single(catalog.Search("amber beacon")).Key);
        Assert.Single(catalog.Search("beacon", folderScope: @"D:\other"));
        Assert.Empty(catalog.Search("beacon", folderScope: @"C:\repos"));
        Assert.Equal(changed, catalog.Get(item.Key));
    }

    [Fact]
    public void GetHeader_ReturnsTheRecordWithoutItsBody()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var item = new Conversation("claude", "01234567-89ab-4def-8123-456789abcdef", "Cobalt title", @"C:\repos\alpha",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, "source.jsonl"), _root, new string('x', 100_000), TextIndexed: false, TextNote: "note");
        catalog.Upsert(item);
        Assert.Equal(item with { Body = "" }, catalog.GetHeader(item.Key));
        Assert.Equal(item, catalog.Get(item.Key));
        Assert.Null(catalog.GetHeader("claude:missing"));
        var names = new Dictionary<string, string> { [item.Key] = "Blue Maple migration" };
        Assert.Equal("", Assert.Single(catalog.Search("maple", aliases: names)).Body);
    }

    [Fact]
    public void GetBodyStart_ReturnsTheFirstCharactersAndSaysWhetherTheTranscriptGoesOn()
    {
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var body = "You\nfirst ask 🔌 with an emoji\n\nClaude\n" + new string('y', 50_000);
        var item = new Conversation("claude", "01234567-89ab-4def-8123-456789abcdef", "Cobalt title", @"C:\repos\alpha",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(_root, "source.jsonl"), _root, body);
        catalog.Upsert(item);
        var start = catalog.GetBodyStart(item.Key, 20)!;
        Assert.True(start.Truncated);
        Assert.StartsWith(start.Text, body);
        // SQLite counts characters, .NET UTF-16 units: the emoji is one character there and two here, so the start is never shorter than asked.
        Assert.True(start.Text.Length >= 20 && start.Text.Length <= 21, start.Text.Length.ToString());
        var whole = catalog.GetBodyStart(item.Key, body.Length + 1)!;
        Assert.Equal(body, whole.Text);
        Assert.False(whole.Truncated);
        Assert.Equal(body, catalog.GetBodyStart(item.Key, body.Length)!.Text);
        Assert.False(catalog.GetBodyStart(item.Key, body.Length)!.Truncated);
        Assert.Null(catalog.GetBodyStart("claude:missing", 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.GetBodyStart(item.Key, 0));
    }

    [Fact]
    public void Constructor_BringsAFirstVersionCatalogForwardWithItsRowsSearchable()
    {
        var path = Path.Combine(_root, "catalog.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE conversations (key TEXT PRIMARY KEY, agent TEXT NOT NULL, title TEXT NOT NULL, updated INTEGER NOT NULL, data TEXT NOT NULL, body TEXT NOT NULL);
                CREATE VIRTUAL TABLE conversation_search USING fts5(key UNINDEXED,title,body,folder);
                """;
            command.ExecuteNonQuery();
        }
        Conversation Make(string id, string folder, string body) => new("claude", id, "Title " + id, folder,
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), Path.Combine(_root, id + ".jsonl"), _root, body);
        var alpha = Make("11111111-1111-4111-8111-111111111111", @"C:\repos\alpha", "cobalt lighthouse");
        var beta = Make("22222222-2222-4222-8222-222222222222", @"C:\repos\beta", "amber beacon");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            foreach (var item in new[] { alpha, beta })
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO conversations(key,agent,title,updated,data,body) VALUES($key,$agent,$title,$updated,$data,$body);
                    INSERT INTO conversation_search(key,title,body,folder) VALUES($key,$title,$body,$folder);
                    """;
                command.Parameters.AddWithValue("$key", item.Key);
                command.Parameters.AddWithValue("$agent", item.AgentId);
                command.Parameters.AddWithValue("$title", item.Title);
                command.Parameters.AddWithValue("$updated", item.UpdatedAt.UtcTicks);
                command.Parameters.AddWithValue("$data", System.Text.Json.JsonSerializer.Serialize(item with { Body = "" }));
                command.Parameters.AddWithValue("$body", item.Body);
                command.Parameters.AddWithValue("$folder", item.WorkingDirectory!);
                command.ExecuteNonQuery();
            }
        }
        var catalog = new ConversationCatalog(path);
        Assert.Equal(alpha.Key, Assert.Single(catalog.Search("cobalt")).Key);
        Assert.Equal(beta.Key, Assert.Single(catalog.Search("", folderScope: @"C:\repos\beta")).Key);
        Assert.Equal(alpha, catalog.Get(alpha.Key));
        Assert.Empty(catalog.Fingerprints());
        catalog.Upsert(alpha with { Body = "sapphire lantern" });
        Assert.Empty(catalog.Search("cobalt"));
        Assert.Single(catalog.Search("sapphire"));
        var again = new ConversationCatalog(path);
        Assert.Equal(2, again.Search("").Count);
        Assert.Single(again.Search("sapphire"));
    }
}
