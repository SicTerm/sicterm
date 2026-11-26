using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sic.Core.Agents;
using Sic.Core.Limits;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AgentContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-agent-context-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private const string CodexId = "01a14526-de57-7fa0-9c16-259122354299";

    public AgentContextTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string TurnContext(string model) => JsonSerializer.Serialize(new { timestamp = "2026-10-16T14:39:00.127Z", type = "turn_context", payload = new { cwd = @"C:\repo", approval_policy = "on-request", model, summary = "none" } });
    private static string TokenCount(long total, long? window) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-10-16T14:39:02.263Z", type = "event_msg",
        payload = new { type = "token_count", info = new { total_token_usage = new { total_tokens = total }, last_token_usage = new { input_tokens = total - 14, cached_input_tokens = 13184, output_tokens = 14, reasoning_output_tokens = 0, total_tokens = total }, model_context_window = window }, rate_limits = (object?)null }
    });

    /// <summary>Codex's own rule: remaining = round((window − 12,000 − max(0, total − 12,000)) / (window − 12,000) × 100), used = 100 − remaining.</summary>
    [Theory]
    [InlineData(17007, 475000, 1)]
    [InlineData(120000, 475000, 23)]
    [InlineData(12000, 475000, 0)]
    [InlineData(475000, 475000, 100)]
    [InlineData(600000, 475000, 100)]
    [InlineData(5000, 12000, 100)]
    public void TheShareFollowsCodexsBaselineRule(long total, long window, double expected) => Assert.Equal(expected, CodexContext.UsedPercent(total, window));

    [Fact]
    public void ACodexRolloutGivesTheModelOfItsLastTurnAndTheUsageOfItsLastResponse()
    {
        var path = Path.Combine(_root, "rollout-2026-10-16T09-38-57-" + CodexId + ".jsonl");
        File.WriteAllLines(path, [
            JsonSerializer.Serialize(new { timestamp = "2026-10-16T14:38:59Z", type = "session_meta", payload = new { id = CodexId, cwd = @"C:\repo" } }),
            TurnContext("gpt-6-sol"), TokenCount(17007, 475000),
            TurnContext("gpt-6.1-sol"), JsonSerializer.Serialize(new { type = "event_msg", payload = new { type = "token_count", info = (object?)null, rate_limits = new { } } }), TokenCount(120000, 475000)]);
        var reading = CodexContext.Read(CodexId, path, Now)!;
        Assert.Equal(new ContextReading(CodexId, 23, 475000, 120000, "gpt-6.1-sol", Now), reading);
        Assert.Equal("Context window · 120k of 475k tokens · gpt-6.1-sol", ContextWindow.Describe(reading));
        // No window: the model and the tokens, no share. No usage at all: the model alone.
        File.WriteAllLines(path, [TurnContext("gpt-6.1-sol"), TokenCount(120000, null)]);
        Assert.Equal(new ContextReading(CodexId, null, null, 120000, "gpt-6.1-sol", Now), CodexContext.Read(CodexId, path, Now));
        File.WriteAllLines(path, [TurnContext("gpt-6.1-sol"), "{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_started\"}}", "{broken"]);
        Assert.Equal(new ContextReading(CodexId, null, null, null, "gpt-6.1-sol", Now), CodexContext.Read(CodexId, path, Now));
        File.WriteAllLines(path, [JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = CodexId } })]);
        Assert.Null(CodexContext.Read(CodexId, path, Now));
        Assert.Null(CodexContext.Read(CodexId, path + ".missing", Now));
    }

    [Fact]
    public void ACodexTurnLongerThanTheTailStillNamesItsModelFromTheHead()
    {
        var path = Path.Combine(_root, "rollout-long.jsonl");
        var filler = JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", content = new string('x', 4000) } });
        File.WriteAllLines(path, [TurnContext("gpt-6.1-sol"), .. Enumerable.Repeat(filler, 120), TokenCount(300000, 475000)]);
        Assert.True(new FileInfo(path).Length > 256 * 1024);
        var reading = CodexContext.Read(CodexId, path, Now)!;
        Assert.Equal("gpt-6.1-sol", reading.Model);
        Assert.Equal(CodexContext.UsedPercent(300000, 475000), reading.UsedPercent);
    }

    [Fact]
    public void AnOpenCodeSessionGivesItsModelAndTheUsageAfterItsLastCompaction()
    {
        var home = Path.Combine(_root, "opencode");
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, "opencode.db");
        var catalog = JsonSerializer.Serialize(new { openai = new { id = "openai", models = new Dictionary<string, object> { ["gpt-5.6-sol-fast"] = new { limit = new { context = 400000, output = 128000 } } } } });
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE session_v2(id TEXT PRIMARY KEY, parent_id TEXT, directory TEXT, title TEXT, model TEXT, time_created INTEGER, time_updated INTEGER);
                CREATE TABLE session_message(id TEXT PRIMARY KEY, session_id TEXT, type TEXT, seq INTEGER, time_created INTEGER, time_updated INTEGER, data TEXT);
                CREATE TABLE kv(key TEXT PRIMARY KEY, value TEXT, time_created INTEGER, time_updated INTEGER);
                INSERT INTO session_v2 VALUES('ses_one', NULL, 'C:/repo', 'A session', '{"id":"gpt-5.6-sol-fast","providerID":"openai","variant":"high"}', 1, 2);
                INSERT INTO session_message VALUES('m1','ses_one','user',1,1,1,'{"content":[]}');
                INSERT INTO session_message VALUES('m2','ses_one','assistant',2,1,1,'{"model":{"id":"gpt-5.6-sol-fast","providerID":"openai"},"tokens":{"input":300000,"output":100,"reasoning":0,"cache":{"read":50000,"write":0}}}');
                INSERT INTO session_message VALUES('m3','ses_one','compaction',3,1,1,'{"status":"completed","tokens":{"input":1,"output":1,"reasoning":0,"cache":{"read":0,"write":0}}}');
                INSERT INTO session_message VALUES('m4','ses_one','assistant',4,1,1,'{"model":{"id":"gpt-5.6-sol-fast","providerID":"openai"},"tokens":{"input":6076,"output":15,"reasoning":9,"cache":{"read":100,"write":200}}}');
                INSERT INTO session_message VALUES('m5','ses_one','assistant',5,1,1,'{"model":{"id":"gpt-5.6-sol-fast","providerID":"openai"},"content":[]}');
                INSERT INTO session_v2 VALUES('ses_fresh', NULL, 'C:/repo', 'Nothing sent', '{"id":"gpt-5.6-sol-fast","providerID":"openai"}', 1, 2);
                INSERT INTO kv VALUES('models-dev:catalog', $catalog, 1, 7);
                """;
            command.Parameters.AddWithValue("$catalog", JsonSerializer.Serialize(new { updatedAt = 7, body = catalog }));
            command.ExecuteNonQuery();
        }
        var agent = new AgentDefinition("opencode", "OpenCode", "opencode", [], home);
        var reading = OpenCodeContext.Read(agent, "ses_one", Now)!;
        // The last assistant message with tokens after the completed compaction: 6076 + 15 + 9 + 100 + 200 = 6400 of 400,000.
        Assert.Equal(new ContextReading("ses_one", 2, 400000, 6400, "gpt-5.6-sol-fast", Now), reading);
        Assert.Equal(new ContextReading("ses_fresh", null, 400000, null, "gpt-5.6-sol-fast", Now), OpenCodeContext.Read(agent, "ses_fresh", Now));
        Assert.Null(OpenCodeContext.Read(agent, "ses_missing", Now));
        // A provider plugin's own window for the model stands over the catalog's.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO kv VALUES('plugin:6f70656e636f6465:models:acct', '[{\"slug\":\"gpt-5.6-sol-fast\",\"context_window\":500000}]', 1, 8);";
            command.ExecuteNonQuery();
        }
        Assert.Equal(new ContextReading("ses_one", 1, 500000, 6400, "gpt-5.6-sol-fast", Now), OpenCodeContext.Read(agent, "ses_one", Now));
        Assert.Null(OpenCodeContext.Read(agent with { HistoryDirectory = Path.Combine(_root, "nowhere") }, "ses_one", Now));
    }

    [Fact]
    public void AHermesSessionGivesItsModelAndThePromptItLastSentAndTheWindowFromHermessFiles()
    {
        var home = Path.Combine(_root, "hermes");
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, "state.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sessions(id TEXT PRIMARY KEY, source TEXT, model TEXT, model_config TEXT, billing_base_url TEXT, started_at REAL);
                INSERT INTO sessions VALUES('20261013_003855_c7dd4d','cli','openai-codex/gpt-6.1-sol','{"max_iterations":150,"_usage_anchor":{"prompt_tokens":16543,"completion_tokens":8}}','https://chatgpt.com/backend-api/codex/',1);
                INSERT INTO sessions VALUES('20261012_234216_e68d5c','cli',NULL,NULL,NULL,1);
                INSERT INTO sessions VALUES('20261012_000000_aaaaaa','cli','claude-opus-4-5','{"max_iterations":150}','https://api.anthropic.com',1);
                """;
            command.ExecuteNonQuery();
        }
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], home);
        // No file names the window yet: the model and the tokens stand alone.
        Assert.Equal(new ContextReading("20261013_003855_c7dd4d", null, null, 16543, "gpt-6.1-sol", Now), HermesContext.Read(agent, "20261013_003855_c7dd4d", Now));
        Assert.Equal("Context window · 17k tokens in use, window size unknown · gpt-6.1-sol", ContextWindow.Describe(HermesContext.Read(agent, "20261013_003855_c7dd4d", Now)!));
        Assert.Null(HermesContext.Read(agent, "20261012_234216_e68d5c", Now));
        Assert.Null(HermesContext.Read(agent, "missing", Now));
        // Hermes's own cache of resolved lengths, by model and endpoint (a trailing slash either side is no difference).
        File.WriteAllText(Path.Combine(home, "context_length_cache.yaml"), "context_lengths:\n  gpt-6.1-sol@https://chatgpt.com/backend-api/codex: 500000\n  'other@https://x': 1\n");
        Assert.Equal(new ContextReading("20261013_003855_c7dd4d", 3, 500000, 16543, "gpt-6.1-sol", Now), HermesContext.Read(agent, "20261013_003855_c7dd4d", Now));
        // A model the cache does not name: config.yaml's context_length, then the models.dev catalog, in that order.
        var claude = "20261012_000000_aaaaaa";
        Assert.Equal(new ContextReading(claude, null, null, null, "claude-opus-4-5", Now), HermesContext.Read(agent, claude, Now));
        File.WriteAllText(Path.Combine(home, "models_dev_cache.json"), JsonSerializer.Serialize(new { anthropic = new { models = new Dictionary<string, object> { ["claude-opus-4-5"] = new { limit = new { context = 200000 } } } } }));
        Assert.Equal(200000, HermesContext.Read(agent, claude, Now)!.WindowTokens);
        File.WriteAllText(Path.Combine(home, "config.yaml"), "model:\n  provider: \"anthropic\"\n  context_length: 150000 # trimmed\n");
        Assert.Equal(150000, HermesContext.Read(agent, claude, Now)!.WindowTokens);
        // For a Hermes that does not keep the row current (no route in its model_config), the model the session's last request
        // ran on stands over the row's: the newest main-loop row of session_model_usage. A title call's row, newer still, runs
        // on a route of its own and does not count, and a session with no call yet keeps its row's model. The window is looked
        // up by that request's endpoint first.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE session_model_usage(session_id TEXT, model TEXT, billing_provider TEXT, billing_base_url TEXT, billing_mode TEXT, task TEXT, last_seen REAL);
                INSERT INTO session_model_usage VALUES('20261012_000000_aaaaaa','claude-opus-4-5','anthropic','https://api.anthropic.com','','',10);
                INSERT INTO session_model_usage VALUES('20261012_000000_aaaaaa','claude-opus-5-5','anthropic','https://fallback.example','','',20);
                INSERT INTO session_model_usage VALUES('20261012_000000_aaaaaa','claude-haiku-5-5','anthropic','https://api.anthropic.com','','title_generation',30);
                """;
            command.ExecuteNonQuery();
        }
        Assert.Equal(new ContextReading(claude, null, 150000, null, "claude-opus-5-5", Now), HermesContext.Read(agent, claude, Now));
        File.AppendAllText(Path.Combine(home, "context_length_cache.yaml"), "  claude-opus-5-5@https://fallback.example: 1000000\n");
        Assert.Equal(1000000, HermesContext.Read(agent, claude, Now)!.WindowTokens);
        // A Hermes that keeps the row current writes the model each turn ran on to it, with the turn's route under
        // model_config.gateway_runtime: the row is read then, so a /model switch shows at once, as Hermes's own status line
        // shows it, and not with the first reply on the new model (November 7, 2026). The window is looked up by the row's
        // endpoint first.
        void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        Execute("""UPDATE sessions SET model='claude-opus-4-5', model_config='{"gateway_runtime":{"provider":"anthropic","base_url":"https://api.anthropic.com","api_mode":"anthropic_messages","fallback_active":false}}' WHERE id='20261012_000000_aaaaaa';""");
        Assert.Equal(new ContextReading(claude, null, 150000, null, "claude-opus-4-5", Now), HermesContext.Read(agent, claude, Now));
        // /model: the row's model and its top-level route move at once; gateway_runtime stays as the last turn left it.
        Execute("""UPDATE sessions SET model='claude-fable-5-1', model_config='{"provider":"anthropic","gateway_runtime":{"provider":"openai-codex","base_url":"https://chatgpt.com/backend-api/codex","api_mode":"codex_responses","fallback_active":false}}' WHERE id='20261012_000000_aaaaaa';""");
        Assert.Equal(new ContextReading(claude, null, 150000, null, "claude-fable-5-1", Now), HermesContext.Read(agent, claude, Now));
        // A turn the fallback chain moved: the row carries the model that answered, and the window follows its endpoint.
        Execute("""UPDATE sessions SET model='claude-opus-5-5', model_config='{"gateway_runtime":{"provider":"anthropic","base_url":"https://fallback.example","fallback_active":true}}' WHERE id='20261012_000000_aaaaaa';""");
        Assert.Equal(new ContextReading(claude, null, 1000000, null, "claude-opus-5-5", Now), HermesContext.Read(agent, claude, Now));
        // A row kept current with no model falls back to the last request's.
        Execute("""UPDATE sessions SET model=NULL WHERE id='20261012_000000_aaaaaa';""");
        Assert.Equal("claude-opus-5-5", HermesContext.Read(agent, claude, Now)!.Model);
        Assert.Equal("gpt-6.1-sol", HermesContext.Read(agent, "20261013_003855_c7dd4d", Now)!.Model);
        Assert.Equal("gpt-6.1-sol", HermesContext.DisplayModel("openai-codex/gpt-6.1-sol"));
        Assert.Equal("gpt-6.1-sol", HermesContext.DisplayModel("gpt-6.1-sol"));
    }

    [Fact]
    public void AHermesSessionsProviderIsItsBillingProviderElseItsModelsPrefix()
    {
        var home = Path.Combine(_root, "hermes-provider");
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, "state.db");
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], home);
        Assert.Null(HermesContext.Provider(agent, "20261013_003855_c7dd4d"));
        // A Hermes before the billing columns: the model's prefix, or a Claude model, says.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sessions(id TEXT PRIMARY KEY, source TEXT, model TEXT, started_at REAL);
                INSERT INTO sessions VALUES('20261013_003855_c7dd4d','cli','openai-codex/gpt-6.1-sol',1);
                INSERT INTO sessions VALUES('20261012_000000_aaaaaa','cli','claude-opus-4-5',1);
                INSERT INTO sessions VALUES('20261012_000000_bbbbbb','cli','gpt-6.1-sol-900k',1);
                INSERT INTO sessions VALUES('20261012_234216_e68d5c','cli',NULL,1);
                """;
            command.ExecuteNonQuery();
        }
        Assert.Equal("openai-codex", HermesContext.Provider(agent, "20261013_003855_c7dd4d"));
        Assert.Equal("anthropic", HermesContext.Provider(agent, "20261012_000000_aaaaaa"));
        Assert.Null(HermesContext.Provider(agent, "20261012_000000_bbbbbb"));
        Assert.Null(HermesContext.Provider(agent, "20261012_234216_e68d5c"));
        Assert.Null(HermesContext.Provider(agent, "missing"));
        // With the column, the provider Hermes billed the session to stands over the model's name, and an empty one is none.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE sessions ADD COLUMN billing_provider TEXT;
                UPDATE sessions SET billing_provider='anthropic' WHERE id='20261012_000000_bbbbbb';
                UPDATE sessions SET billing_provider='' WHERE id='20261013_003855_c7dd4d';
                """;
            command.ExecuteNonQuery();
        }
        Assert.Equal("anthropic", HermesContext.Provider(agent, "20261012_000000_bbbbbb"));
        Assert.Equal("openai-codex", HermesContext.Provider(agent, "20261013_003855_c7dd4d"));
        // The provider of the session's last request, from session_model_usage, stands over the row's, which Hermes fills once;
        // a row with an empty provider, or a session without one, leaves the answer as it was.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE session_model_usage(session_id TEXT, model TEXT, billing_provider TEXT, billing_base_url TEXT, billing_mode TEXT, task TEXT, last_seen REAL);
                INSERT INTO session_model_usage VALUES('20261013_003855_c7dd4d','gpt-6.1-sol','openai-codex','','','',10);
                INSERT INTO session_model_usage VALUES('20261013_003855_c7dd4d','claude-opus-5-5','anthropic','','','',20);
                INSERT INTO session_model_usage VALUES('20261012_000000_aaaaaa','claude-opus-4-5','','','','',20);
                """;
            command.ExecuteNonQuery();
        }
        Assert.Equal("anthropic", HermesContext.Provider(agent, "20261013_003855_c7dd4d"));
        Assert.Equal("anthropic", HermesContext.Provider(agent, "20261012_000000_aaaaaa"));
        Assert.Equal("anthropic", HermesContext.Provider(agent, "20261012_000000_bbbbbb"));
        // A row Hermes keeps current says what the session draws on, read as Hermes reads it back: the last turn's route under
        // gateway_runtime, which Hermes resumes the session on, before the top-level keys a /model switch writes, and either
        // before the last request's; a row with neither leaves the last request's provider to stand, as above.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE sessions ADD COLUMN model_config TEXT;
                UPDATE sessions SET model_config='{"provider":"anthropic","gateway_runtime":{"provider":"openai-codex","base_url":"https://chatgpt.com/backend-api/codex"}}' WHERE id='20261012_000000_bbbbbb';
                UPDATE sessions SET model_config='{"provider":"openai-codex","api_mode":"codex_responses"}' WHERE id='20261013_003855_c7dd4d';
                UPDATE sessions SET model_config='{"max_iterations":150}' WHERE id='20261012_000000_aaaaaa';
                """;
            command.ExecuteNonQuery();
        }
        Assert.Equal("openai-codex", HermesContext.Provider(agent, "20261012_000000_bbbbbb"));
        Assert.Equal("openai-codex", HermesContext.Provider(agent, "20261013_003855_c7dd4d"));
        Assert.Equal("anthropic", HermesContext.Provider(agent, "20261012_000000_aaaaaa"));
    }

    [Fact]
    public void HermesModelConfigReadsTheModelBlocksKeysInOnePass()
    {
        Assert.Empty(HermesContext.ModelConfig(_root, "base_url", "context_length"));
        File.WriteAllText(Path.Combine(_root, "config.yaml"),
            "base_url: top-level-is-not-it\nmodel:\n  base_url: 'https://x/v1' # the endpoint\n  context_length:\n    nested: 1\n  context_length: 9\nother:\n  base_url: elsewhere\n");
        var values = HermesContext.ModelConfig(_root, "base_url", "context_length");
        Assert.Equal("https://x/v1", values["base_url"]);
        // The first line of a key answers for it, an empty one included, as the provider has always been read.
        Assert.False(values.ContainsKey("context_length"));
        Assert.Equal("https://x/v1", HermesContext.ConfigValue(_root, "base_url"));
    }
}
