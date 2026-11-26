using System.Text.Json;
using Sic.Core.Agents;
using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class HistoryIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "command-central-index-tests-" + Guid.NewGuid().ToString("N"));
    public HistoryIndexerTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Refresh_DiscoversExternalHistoryUpdatesSearchAndReportsUnreadableRecords()
    {
        var project = Path.Combine(_root, "projects", "fixture-project");
        Directory.CreateDirectory(project);
        var path = Path.Combine(project, "01234567-89ab-4def-8123-456789abcdef.jsonl");
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "user", sessionId = "01234567-89ab-4def-8123-456789abcdef", cwd = project, message = new { role = "user", content = "cobalt lighthouse" } }) + "\n");
        File.WriteAllText(Path.Combine(project, "invalid.jsonl"), "{broken\n");
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var indexer = new HistoryIndexer(catalog);
        AgentDefinition[] agents = [new("claude", "Claude", "claude", [], _root)];
        var first = indexer.Refresh(agents);
        Assert.Equal(1, first.Imported);
        Assert.Equal(1, first.Failed);
        Assert.Equal(0, first.MetadataOnly);
        Assert.Single(first.Warnings);
        Assert.Single(catalog.Search("cobalt"));
        var second = indexer.Refresh(agents);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Unchanged);
        File.AppendAllText(path, JsonSerializer.Serialize(new { type = "assistant", sessionId = "01234567-89ab-4def-8123-456789abcdef", message = new { role = "assistant", content = "new sapphire text" } }) + "\n");
        Assert.Equal(1, indexer.Refresh(agents).Imported);
        Assert.Single(catalog.Search("sapphire"));
    }

    [Fact]
    public void Refresh_ImportsOversizedTranscriptsAsMetadataInsteadOfSkippingThem()
    {
        const string smallId = "01234567-89ab-4def-8123-456789abcdef";
        const string largeId = "ec293be8-e3af-4365-8862-633825531a60";
        var project = Path.Combine(_root, "projects", "fixture-project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, smallId + ".jsonl"),
            JsonSerializer.Serialize(new { type = "user", sessionId = smallId, cwd = project, message = new { role = "user", content = "cobalt lighthouse" } }) + "\n");
        var large = Path.Combine(project, largeId + ".jsonl");
        File.WriteAllLines(large, new object[]
        {
            new { type = "user", sessionId = largeId, cwd = project, timestamp = "2026-09-26T16:30:00Z", message = new { role = "user", content = "amber beacon question" } },
            new { type = "assistant", sessionId = largeId, cwd = project, timestamp = "2026-09-26T16:31:00Z", message = new { role = "assistant", content = new string('z', 4096) + " hiddenneedle" } }
        }.Select(row => JsonSerializer.Serialize(row)));
        var original = File.ReadAllBytes(large);
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var indexer = new HistoryIndexer(catalog, fullTextLimit: 2048);
        AgentDefinition[] agents = [new("claude", "Claude", "claude", [], _root)];
        var result = indexer.Refresh(agents);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.MetadataOnly);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Warnings);
        var entry = catalog.Get("claude:" + largeId)!;
        Assert.False(entry.TextIndexed);
        Assert.Contains("MiB", entry.TextNote);
        Assert.Equal(project, entry.WorkingDirectory);
        Assert.Equal(DateTimeOffset.Parse("2026-09-26T16:31:00Z"), entry.UpdatedAt);
        Assert.StartsWith("You\namber beacon question", entry.Body);
        Assert.Single(catalog.Search("amber beacon"));
        Assert.Empty(catalog.Search("hiddenneedle"));
        Assert.True(catalog.Get("claude:" + smallId)!.TextIndexed);
        Assert.Equal(2, indexer.Refresh(agents).Unchanged);
        Assert.Equal(original, File.ReadAllBytes(large));
    }

    /// <summary>
    /// Many transcripts, parsed several at a time and written one at a time: every one lands once, the bad ones are reported by
    /// path, a second refresh reads none again, and a change to one is picked up on its own.
    /// </summary>
    [Fact]
    public void Refresh_ParsesManyTranscriptsTogetherAndWritesEveryOneOnce()
    {
        var project = Path.Combine(_root, "projects", "fixture-project");
        Directory.CreateDirectory(project);
        var ids = Enumerable.Range(0, 60).Select(index => $"{index:x8}-89ab-4def-8123-456789abcdef").ToArray();
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
            File.WriteAllText(Path.Combine(project, id + ".jsonl"),
                JsonSerializer.Serialize(new { type = "user", sessionId = id, cwd = project, message = new { role = "user", content = "needle" + index + " " + new string('z', 20_000) } }) + "\n");
        File.WriteAllText(Path.Combine(project, "broken-one.jsonl"), "{broken\n");
        File.WriteAllText(Path.Combine(project, "broken-two.jsonl"), "{broken\n");
        var catalog = new ConversationCatalog(Path.Combine(_root, "catalog.db"));
        var indexer = new HistoryIndexer(catalog);
        AgentDefinition[] agents = [new("claude", "Claude", "claude", [], _root)];
        var first = indexer.Refresh(agents);
        Assert.Equal((60, 0, 2), (first.Imported, first.Unchanged, first.Failed));
        Assert.Equal(2, first.Warnings.Count);
        Assert.All(first.Warnings, warning => Assert.StartsWith(Path.Combine(project, "broken-"), warning));
        Assert.Equal(60, catalog.Search("", limit: 500).Count);
        Assert.Single(catalog.Search("needle42"));
        Assert.Equal(60, catalog.Fingerprints().Count);
        var second = indexer.Refresh(agents);
        Assert.Equal((0, 60, 2), (second.Imported, second.Unchanged, second.Failed));
        var changed = Path.Combine(project, ids[7] + ".jsonl");
        File.AppendAllText(changed, JsonSerializer.Serialize(new { type = "assistant", sessionId = ids[7], message = new { role = "assistant", content = "sapphire reply" } }) + "\n");
        File.SetLastWriteTimeUtc(changed, File.GetLastWriteTimeUtc(changed).AddSeconds(2));
        var third = indexer.Refresh(agents);
        Assert.Equal((1, 59, 2), (third.Imported, third.Unchanged, third.Failed));
        Assert.Equal(ids[7], Assert.Single(catalog.Search("sapphire")).SessionId);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => indexer.Refresh(agents, cancelled.Token));
    }

    [Fact]
    public void Refresh_KeepsItsFingerprintsInTheCatalogSoANewIndexerReadsOnlyChangedSources()
    {
        const string id = "01234567-89ab-4def-8123-456789abcdef";
        var project = Path.Combine(_root, "projects", "fixture-project");
        Directory.CreateDirectory(project);
        var path = Path.Combine(project, id + ".jsonl");
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "user", sessionId = id, cwd = project, message = new { role = "user", content = "cobalt lighthouse" } }) + "\n");
        var databasePath = Path.Combine(_root, "catalog.db");
        AgentDefinition[] agents = [new("claude", "Claude", "claude", [], _root)];
        var first = new HistoryIndexer(new ConversationCatalog(databasePath)).Refresh(agents);
        Assert.Equal(1, first.Imported);
        // A new launch: a new catalog object over the same file, and a new indexer with nothing in memory.
        var catalog = new ConversationCatalog(databasePath);
        var indexer = new HistoryIndexer(catalog);
        var second = indexer.Refresh(agents);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Unchanged);
        Assert.Single(catalog.Search("cobalt"));
        File.AppendAllText(path, JsonSerializer.Serialize(new { type = "assistant", sessionId = id, message = new { role = "assistant", content = "new sapphire text" } }) + "\n");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        var third = indexer.Refresh(agents);
        Assert.Equal(1, third.Imported);
        Assert.Equal(0, third.Unchanged);
        Assert.Single(catalog.Search("sapphire"));
        Assert.Equal(1, new HistoryIndexer(new ConversationCatalog(databasePath)).Refresh(agents).Unchanged);
    }
}
