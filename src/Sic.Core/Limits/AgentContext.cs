using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Sic.Core.Agents;
using Sic.Core.Sessions;

namespace Sic.Core.Limits;

/// <summary>
/// A Codex session's model and context window, from its rollout log (docs/decisions/context-window.md). The last
/// <c>turn_context</c> record names the model (a slug, as Codex's own interface maps it to a display name only in memory), and
/// the last <c>event_msg</c> of type <c>token_count</c> with an <c>info</c> carries the last response's usage and the usable
/// window, already 95 % of the model's; Codex's own percentage takes 12,000 tokens off both for the prompt, the tools and the
/// room a compaction needs, which is what <see cref="UsedPercent"/> does. Read from the tail of the log, where both records
/// are: the usage follows every response, the turn context opens every turn; a turn longer than the tail falls back to the
/// first turn context at the head of the log.
/// </summary>
public static class CodexContext
{
    /// <summary>Codex's BASELINE_TOKENS: what it takes off the window and the usage before working out the share remaining.</summary>
    public const long BaselineTokens = 12_000;
    private const int TailBytes = 256 * 1024;
    private const int HeadBytes = 64 * 1024;

    /// <summary>The share used as Codex shows it: 100 less the share remaining of the window past the baseline; 100 for a window within the baseline.</summary>
    public static double UsedPercent(long totalTokens, long contextWindow)
    {
        if (contextWindow <= BaselineTokens) return 100;
        var effective = contextWindow - BaselineTokens;
        var used = Math.Max(0, totalTokens - BaselineTokens);
        var remaining = Math.Max(0, effective - used);
        return 100 - Math.Clamp(Math.Round(100.0 * remaining / effective), 0, 100);
    }

    /// <summary>The session's reading from its rollout at <paramref name="path"/>, or null when the log names no model and holds no usage.</summary>
    public static ContextReading? Read(string sessionId, string path, DateTimeOffset readAt)
    {
        string tail;
        long start;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            tail = reader.ReadToEnd();
            if (start > 0 && tail.IndexOf('\n') is var first and >= 0) tail = tail[(first + 1)..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        string? model = null;
        long? total = null, window = null;
        var usageFound = false;
        foreach (var line in tail.Split('\n').Reverse())
        {
            if (!usageFound && line.Contains("\"token_count\"", StringComparison.Ordinal) && Usage(line) is { } usage) { (total, window) = usage; usageFound = true; }
            if (model is null && line.Contains("\"turn_context\"", StringComparison.Ordinal)) model = TurnModel(line);
            if (usageFound && model is not null) break;
        }
        // A long turn can push its turn context out of the tail; the first one in the log is at its head, read only then.
        if (model is null && start > 0) model = HeadModel(path, (int)Math.Min(HeadBytes, start));
        if (model is null && !usageFound) return null;
        var percent = total is { } tokens && window is { } size ? UsedPercent(tokens, size) : (double?)null;
        return new ContextReading(sessionId, percent, window, total, model, readAt);
    }

    /// <summary>The model of the first turn context in the log's first <paramref name="bytes"/>, or null.</summary>
    private static string? HeadModel(string path, int bytes)
    {
        string head;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[bytes];
            var read = stream.ReadAtLeast(buffer, bytes, throwOnEndOfStream: false);
            head = Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        foreach (var line in head.Split('\n'))
            if (line.Contains("\"turn_context\"", StringComparison.Ordinal) && TurnModel(line) is { } first) return first;
        return null;
    }

    /// <summary>The last response's total tokens and the usable window from a token_count record with an info, or null for any other line.</summary>
    private static (long Total, long? Window)? Usage(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
                || Text(payload, "type") != "token_count" || !payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object
                || !info.TryGetProperty("last_token_usage", out var last) || last.ValueKind != JsonValueKind.Object || Integer(last, "total_tokens") is not { } total)
                return null;
            return (total, Integer(info, "model_context_window"));
        }
        catch (JsonException) { return null; }
    }

    private static string? TurnModel(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "turn_context" || !root.TryGetProperty("payload", out var payload)) return null;
            return Text(payload, "model") is { Length: > 0 } model ? model : null;
        }
        catch (JsonException) { return null; }
    }

    internal static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static long? Integer(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt64(out var whole) ? whole : value.TryGetDouble(out var real) ? (long)real : null : null;
}

/// <summary>
/// An OpenCode session's model and context window, from opencode.db: the session row's model, and what OpenCode's own
/// interface shows as context usage, the tokens of the last assistant message that carries any after the last completed
/// compaction (input, output, reasoning, cache read and cache write together) over the model's context limit. The limit is the
/// models.dev catalog's, which OpenCode keeps in the same database's kv table, with a provider plugin's own figure for its
/// models (the ChatGPT plugin lists its models with their windows there too) standing over it. Read-only; the account and
/// credential tables are never touched.
/// </summary>
public static class OpenCodeContext
{
    private const string CatalogKey = "models-dev:catalog";
    /// <summary>The catalog is a 5 MB JSON document; each model's limit is kept once per catalog version.</summary>
    private static readonly ConcurrentDictionary<string, (long Stamp, long? Context)> Limits = new(StringComparer.Ordinal);

    public static ContextReading? Read(AgentDefinition agent, string sessionId, DateTimeOffset readAt)
    {
        var path = Path.Combine(agent.HistoryDirectory, OpenCodeHistoryReader.DatabaseName);
        if (!File.Exists(path)) return null;
        using var connection = SqliteHistory.OpenReadOnly(path);
        var sessionColumns = SqliteHistory.Columns(connection, "session_v2");
        if (!sessionColumns.Contains("id") || !sessionColumns.Contains("model")) return null;
        string? modelId = null, providerId = null;
        using (var session = connection.CreateCommand())
        {
            session.CommandText = "SELECT model FROM session_v2 WHERE id=$id LIMIT 1";
            session.Parameters.AddWithValue("$id", sessionId);
            if (session.ExecuteScalar() is string model)
            {
                try
                {
                    using var document = JsonDocument.Parse(model);
                    modelId = CodexContext.Text(document.RootElement, "id") ?? CodexContext.Text(document.RootElement, "modelID");
                    providerId = CodexContext.Text(document.RootElement, "providerID");
                }
                catch (JsonException) { }
            }
        }
        long? tokens = null;
        var messageColumns = SqliteHistory.Columns(connection, "session_message");
        if (new[] { "session_id", "type", "seq", "data" }.All(messageColumns.Contains))
        {
            using var last = connection.CreateCommand();
            last.CommandText = """
                SELECT data FROM session_message WHERE session_id=$id AND type='assistant' AND json_extract(data,'$.tokens') IS NOT NULL
                  AND seq > COALESCE((SELECT MAX(seq) FROM session_message WHERE session_id=$id AND type='compaction' AND json_extract(data,'$.status')='completed'), -1)
                ORDER BY seq DESC LIMIT 1
                """;
            last.Parameters.AddWithValue("$id", sessionId);
            if (last.ExecuteScalar() is string data)
            {
                try
                {
                    using var document = JsonDocument.Parse(data);
                    var root = document.RootElement;
                    if (root.TryGetProperty("tokens", out var usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        var cache = usage.TryGetProperty("cache", out var cached) && cached.ValueKind == JsonValueKind.Object ? cached : default;
                        tokens = (CodexContext.Integer(usage, "input") ?? 0) + (CodexContext.Integer(usage, "output") ?? 0) + (CodexContext.Integer(usage, "reasoning") ?? 0)
                            + (CodexContext.Integer(cache, "read") ?? 0) + (CodexContext.Integer(cache, "write") ?? 0);
                    }
                    // The message's own model stands over the session's, which a switch after it would have changed.
                    if (root.TryGetProperty("model", out var messageModel) && messageModel.ValueKind == JsonValueKind.Object)
                    {
                        modelId = CodexContext.Text(messageModel, "id") ?? CodexContext.Text(messageModel, "modelID") ?? modelId;
                        providerId = CodexContext.Text(messageModel, "providerID") ?? providerId;
                    }
                }
                catch (JsonException) { }
            }
        }
        if (modelId is null && tokens is null) return null;
        var window = modelId is null ? null : ContextLimit(connection, path, providerId, modelId);
        var percent = tokens is { } used && window is > 0 ? Math.Clamp(Math.Round(100.0 * used / window.Value), 0, 100) : (double?)null;
        return new ContextReading(sessionId, percent, window, tokens, modelId, readAt);
    }

    /// <summary>The model's context limit from the kv table: a provider plugin's list first, then the models.dev catalog; null when neither names it.</summary>
    private static long? ContextLimit(Microsoft.Data.Sqlite.SqliteConnection connection, string path, string? providerId, string modelId)
    {
        var kvColumns = SqliteHistory.Columns(connection, "kv");
        if (!kvColumns.Contains("key") || !kvColumns.Contains("value")) return null;
        using (var plugins = connection.CreateCommand())
        {
            plugins.CommandText = "SELECT value FROM kv WHERE key LIKE 'plugin:%:models:%'";
            using var rows = plugins.ExecuteReader();
            while (rows.Read())
            {
                if (rows.IsDBNull(0)) continue;
                try
                {
                    using var document = JsonDocument.Parse(rows.GetString(0));
                    if (document.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var model in document.RootElement.EnumerateArray())
                        if (CodexContext.Text(model, "slug") == modelId && CodexContext.Integer(model, "context_window") is > 0 and { } window) return window;
                }
                catch (JsonException) { }
            }
        }
        if (providerId is null) return null;
        // The row's stamp alone first: the catalog's value is megabytes, which SQLite reads whole for any query that selects
        // it, and the answer is kept per stamp, so the value is read only when the catalog has changed.
        var stamped = kvColumns.Contains("time_updated");
        long stamp = 0;
        using (var when = connection.CreateCommand())
        {
            when.CommandText = "SELECT " + (stamped ? "time_updated" : "0") + " FROM kv WHERE key=$key LIMIT 1";
            when.Parameters.AddWithValue("$key", CatalogKey);
            if (when.ExecuteScalar() is not { } found || found is DBNull) return null;
            stamp = Convert.ToInt64(found, CultureInfo.InvariantCulture);
        }
        var cacheKey = path + "|" + providerId + "|" + modelId;
        if (Limits.TryGetValue(cacheKey, out var kept) && kept.Stamp == stamp) return kept.Context;
        using var catalog = connection.CreateCommand();
        catalog.CommandText = "SELECT value FROM kv WHERE key=$key LIMIT 1";
        catalog.Parameters.AddWithValue("$key", CatalogKey);
        if (catalog.ExecuteScalar() is not string value) return null;
        long? limit = null;
        try
        {
            using var envelope = JsonDocument.Parse(value);
            if (CodexContext.Text(envelope.RootElement, "body") is { } body)
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty(providerId, out var provider) && provider.TryGetProperty("models", out var models)
                    && models.TryGetProperty(modelId, out var model) && model.TryGetProperty("limit", out var limits))
                    limit = CodexContext.Integer(limits, "context");
            }
        }
        catch (JsonException) { }
        Limits[cacheKey] = (stamp, limit);
        return limit;
    }
}

/// <summary>
/// A Hermes session's model and context window, as far as Hermes writes them down: the session row's model when Hermes keeps
/// the row current (<see cref="KeptRoute"/>: its unified runtime writes the model each turn ran on, a fallback's included, and
/// a /model switch at once, so the header moves with Hermes's own status line; November 7, 2026), else the model its last
/// request ran on (the newest main-loop row of session_model_usage, <see cref="LastRoute"/>), which stands over the row's for
/// an older Hermes, whose row moved only on /model and missed what its fallback chain did (October 24, 2026); provider-prefixed
/// as Hermes keeps it and shown without the prefix, as Hermes's own interface shows it. And, when the row's model_config
/// carries the usage anchor Hermes keeps for its accounting, that
/// anchor's prompt tokens, the last request's size (input, cache read and cache write), which is what Hermes shows as the
/// context in use. Hermes keeps the window's size in memory, resolved from its config, its own caches and models.dev, so the
/// share is shown only when one of its files on disk gives it: context_length_cache.yaml (the lengths it resolved, by model
/// and endpoint), config.yaml's model.context_length, or models_dev_cache.json (the models.dev catalog). Without one the model
/// and the tokens stand alone.
/// </summary>
public static class HermesContext
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, long? Context)> CatalogLimits = new(StringComparer.Ordinal);

    public static ContextReading? Read(AgentDefinition agent, string sessionId, DateTimeOffset readAt)
    {
        var path = Path.Combine(agent.HistoryDirectory, "state.db");
        if (!File.Exists(path)) return null;
        string? model = null, config = null, baseUrl = null;
        (string Model, string? Provider, string? BaseUrl)? last = null;
        using (var connection = SqliteHistory.OpenReadOnly(path))
        {
            var columns = SqliteHistory.Columns(connection, "sessions");
            if (!columns.Contains("id") || !columns.Contains("model")) return null;
            using (var session = connection.CreateCommand())
            {
                session.CommandText = "SELECT model," + (columns.Contains("model_config") ? "model_config" : "NULL") + "," + (columns.Contains("billing_base_url") ? "billing_base_url" : "NULL")
                    + " FROM sessions WHERE id=$id LIMIT 1";
                session.Parameters.AddWithValue("$id", sessionId);
                using var row = session.ExecuteReader();
                if (!row.Read()) return null;
                model = row.IsDBNull(0) ? null : row.GetString(0);
                config = row.IsDBNull(1) ? null : row.GetString(1);
                baseUrl = row.IsDBNull(2) ? null : row.GetString(2);
            }
            last = LastRoute(connection, sessionId);
        }
        var kept = KeptRoute.Parse(config);
        // The row's model is the session's when Hermes keeps the row current; otherwise the last request's stands over it.
        if ((!kept.Current || string.IsNullOrWhiteSpace(model)) && last is { } route) model = route.Model;
        if (string.IsNullOrWhiteSpace(model)) return null;
        // config.yaml's model block once, for the endpoint and the window it may name.
        var configured = ModelConfig(agent.HistoryDirectory, "base_url", "context_length");
        // The window is looked up by the session's endpoint first: the route the row keeps when it is current, else the last request's.
        var endpoints = kept.Current ? new[] { kept.BaseUrl, last?.BaseUrl } : new[] { last?.BaseUrl, kept.BaseUrl };
        var window = Window(agent.HistoryDirectory, model, [.. endpoints, baseUrl, configured.GetValueOrDefault("base_url")], configured.GetValueOrDefault("context_length"));
        var percent = kept.Prompt is > 0 and { } used && window is > 0 ? Math.Clamp(Math.Round(100.0 * used / window.Value), 0, 100) : (double?)null;
        return new ContextReading(sessionId, percent, window, kept.Prompt, DisplayModel(model), readAt);
    }

    /// <summary>
    /// What the session row's model_config says of the session: whether Hermes keeps the row current, the route it keeps, and
    /// the usage anchor's prompt tokens. Hermes's unified runtime (v0.22, September 2026) writes the model each turn ran on to
    /// the row, with the turn's route under <c>gateway_runtime</c> (a fallback's too, with <c>fallback_active</c>), and a /model
    /// switch writes the row's model with the new route as top-level <c>provider</c>, <c>base_url</c> and <c>api_mode</c>
    /// keys, as its TUI writes the live route; either key means the row is current. The provider and endpoint are read as
    /// Hermes reads them back (<c>session_gateway_runtime</c>): <c>gateway_runtime</c>'s first, then the top-level keys. A
    /// /model switch leaves <c>gateway_runtime</c> as the last turn wrote it until the next turn, and Hermes resumes the session
    /// on that route (seen November 7, 2026), so the limits follow it too. A row without model_config, or one that names neither,
    /// is not kept current.
    /// </summary>
    private readonly record struct KeptRoute(bool Current, string? Provider, string? BaseUrl, long? Prompt)
    {
        public static KeptRoute Parse(string? config)
        {
            if (string.IsNullOrWhiteSpace(config)) return default;
            try
            {
                using var document = JsonDocument.Parse(config);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return default;
                var prompt = root.TryGetProperty("_usage_anchor", out var anchor) ? CodexContext.Integer(anchor, "prompt_tokens") : null;
                var synced = root.TryGetProperty("gateway_runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object;
                var provider = (synced ? Given(runtime, "provider") : null) ?? Given(root, "provider");
                var baseUrl = (synced ? Given(runtime, "base_url") : null) ?? Given(root, "base_url");
                return new(synced || provider is not null, provider, baseUrl, prompt);
            }
            catch (JsonException) { return default; }
        }

        private static string? Given(JsonElement element, string name) => CodexContext.Text(element, name)?.Trim() is { Length: > 0 } value ? value : null;
    }

    /// <summary>The model as Hermes's interface shows it: without a provider prefix.</summary>
    public static string DisplayModel(string model) => model[(model.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The provider the session draws on, from state.db: the one its row keeps when Hermes keeps the row current
    /// (<see cref="KeptRoute"/>; a /model switch moves it at once), else the provider of its last request (<see cref="LastRoute"/>),
    /// else its row's <c>billing_provider</c>, which Hermes fills with the first turn it bills (a session may be on another
    /// provider than config.yaml names, chosen at its start or with /model); for a row without one, or a Hermes before the
    /// column, the prefix of the model when it has one (<c>openai-codex/gpt-6.1-sol</c>), else <see cref="HermesUsage.ClaudeProvider"/>
    /// for a Claude model. Null when the row, the model or the file is missing, when config.yaml's provider stands
    /// (<see cref="ChatGptPlan.HermesConfiguredProvider"/>). Two indexed row reads.
    /// </summary>
    public static string? Provider(AgentDefinition agent, string sessionId)
    {
        var path = Path.Combine(agent.HistoryDirectory, "state.db");
        if (!File.Exists(path)) return null;
        using var connection = SqliteHistory.OpenReadOnly(path);
        var columns = SqliteHistory.Columns(connection, "sessions");
        string? billing = null, model = null, config = null;
        var found = false;
        if (columns.Contains("id"))
        {
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT " + Column("billing_provider") + "," + Column("model") + "," + Column("model_config") + " FROM sessions WHERE id=$id LIMIT 1";
            query.Parameters.AddWithValue("$id", sessionId);
            using var row = query.ExecuteReader();
            if (row.Read())
            {
                found = true;
                billing = row.IsDBNull(0) ? null : row.GetString(0).Trim();
                model = row.IsDBNull(1) ? null : row.GetString(1).Trim();
                config = row.IsDBNull(2) ? null : row.GetString(2);
            }
        }
        var kept = KeptRoute.Parse(config);
        if (kept.Current && kept.Provider is not null) return kept.Provider;
        if (LastRoute(connection, sessionId)?.Provider is { } billed) return billed;
        if (!found) return null;
        if (!string.IsNullOrEmpty(billing)) return billing;
        if (string.IsNullOrEmpty(model)) return null;
        var slash = model.IndexOf('/');
        if (slash > 0) return model[..slash];
        return model.StartsWith("claude", StringComparison.OrdinalIgnoreCase) ? HermesUsage.ClaudeProvider : null;

        string Column(string name) => columns.Contains(name) ? name : "NULL";
    }

    /// <summary>
    /// The route of the session's last main-loop request, from session_model_usage, where Hermes adds every API call's usage
    /// under the model, provider and endpoint that made it, whatever the session row says: the row with the latest last_seen
    /// whose task is empty, since a title or approval call runs on a route of its own. Null for a Hermes before the table, a
    /// session with no call yet, or a row naming no model; an empty provider or endpoint is none.
    /// </summary>
    private static (string Model, string? Provider, string? BaseUrl)? LastRoute(Microsoft.Data.Sqlite.SqliteConnection connection, string sessionId)
    {
        var columns = SqliteHistory.Columns(connection, "session_model_usage");
        if (!columns.Contains("session_id") || !columns.Contains("model") || !columns.Contains("last_seen")) return null;
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT model," + (columns.Contains("billing_provider") ? "billing_provider" : "NULL") + "," + (columns.Contains("billing_base_url") ? "billing_base_url" : "NULL")
            + " FROM session_model_usage WHERE session_id=$id AND model<>''" + (columns.Contains("task") ? " AND task=''" : "") + " ORDER BY last_seen DESC LIMIT 1";
        query.Parameters.AddWithValue("$id", sessionId);
        using var row = query.ExecuteReader();
        if (!row.Read() || row.IsDBNull(0)) return null;
        var model = row.GetString(0).Trim();
        if (model.Length == 0) return null;
        return (model, Blank(row, 1), Blank(row, 2));

        static string? Blank(Microsoft.Data.Sqlite.SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetString(column).Trim() is { Length: > 0 } value ? value : null;
    }

    /// <summary>
    /// The window for <paramref name="model"/> from Hermes's files: context_length_cache.yaml by model and endpoint (any endpoint
    /// when none matches), then config.yaml's model.context_length, then models_dev_cache.json. Null when none has it.
    /// </summary>
    public static long? Window(string hermesHome, string model, IEnumerable<string?> baseUrls) =>
        Window(hermesHome, model, baseUrls, ConfigValue(hermesHome, "context_length"));

    /// <summary><see cref="Window(string, string, IEnumerable{string?})"/> with config.yaml's model.context_length already read, as <see cref="Read"/> has it.</summary>
    private static long? Window(string hermesHome, string model, IEnumerable<string?> baseUrls, string? configuredLength)
    {
        var bare = DisplayModel(model);
        var cached = CachedLengths(Path.Combine(hermesHome, "context_length_cache.yaml"));
        var urls = baseUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url!.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in new[] { model, bare }.Distinct(StringComparer.Ordinal))
        {
            foreach (var url in urls)
                if (cached.TryGetValue(name + "@" + url, out var length) || cached.TryGetValue(name + "@" + url + "/", out length)) return length;
            foreach (var (key, length) in cached)
                if (key.StartsWith(name + "@", StringComparison.Ordinal)) return length;
        }
        if (configuredLength is { } configured && long.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var fromConfig) && fromConfig > 0) return fromConfig;
        return CatalogLimit(Path.Combine(hermesHome, "models_dev_cache.json"), model);
    }

    /// <summary>The lengths Hermes resolved, keyed model@endpoint, from its small YAML cache: one mapping under context_lengths, a line each.</summary>
    private static Dictionary<string, long> CachedLengths(string path)
    {
        var lengths = new Dictionary<string, long>(StringComparer.Ordinal);
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return lengths; }
        foreach (var raw in lines)
        {
            if (raw.Length == 0 || !char.IsWhiteSpace(raw[0])) continue;
            var line = raw.Trim();
            var separator = line.LastIndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0) continue;
            var key = line[..separator].Trim().Trim('"', '\'');
            if (long.TryParse(line[(separator + 2)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length) && length > 0) lengths[key] = length;
        }
        return lengths;
    }

    /// <summary>One value of config.yaml's model block (<see cref="ModelConfig"/>), or null when the file or the key is missing.</summary>
    public static string? ConfigValue(string hermesHome, string key) => ModelConfig(hermesHome, key).GetValueOrDefault(key);

    /// <summary>
    /// The values of <paramref name="keys"/> in config.yaml's model block, from one reading of the file: the <c>model:</c> block
    /// at the left margin and the first line of each key indented under it, quotes and a trailing comment dropped. A plain
    /// reading of the keys, not a YAML parser; a key the file does not set is absent, as is every key when the file is missing.
    /// </summary>
    public static Dictionary<string, string> ModelConfig(string hermesHome, params string[] keys)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] lines;
        try { lines = File.ReadAllLines(Path.Combine(hermesHome, "config.yaml")); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return values; }
        var inModel = false;
        foreach (var raw in lines)
        {
            if (raw.Length == 0 || raw.TrimStart().StartsWith('#')) continue;
            if (!char.IsWhiteSpace(raw[0])) { inModel = raw.TrimEnd() == "model:"; continue; }
            if (!inModel) continue;
            var line = raw.Trim();
            foreach (var key in keys)
            {
                if (values.ContainsKey(key) || !line.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                var value = line[(key.Length + 1)..];
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0) value = value[..comment];
                // The first line of a key answers for it, an empty value included (a block under it is not read).
                values[key] = value.Trim().Trim('"', '\'').Trim();
                break;
            }
            if (values.Count == keys.Length) break;
        }
        foreach (var key in keys) if (values.TryGetValue(key, out var found) && found.Length == 0) values.Remove(key);
        return values;
    }

    /// <summary>The model's limit.context in the models.dev catalog Hermes caches: under its provider when the model is prefixed with one, else the first provider that lists it. Kept per file version.</summary>
    private static long? CatalogLimit(string path, string model)
    {
        DateTime stamp;
        try { if (!File.Exists(path)) return null; stamp = File.GetLastWriteTimeUtc(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        var cacheKey = path + "|" + model;
        if (CatalogLimits.TryGetValue(cacheKey, out var kept) && kept.Stamp == stamp) return kept.Context;
        long? limit = null;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var slash = model.IndexOf('/');
            if (slash > 0 && root.TryGetProperty(model[..slash], out var provider)) limit = Limit(provider, model[(slash + 1)..]);
            if (limit is null)
                foreach (var candidate in root.EnumerateObject())
                    if (Limit(candidate.Value, DisplayModel(model)) is { } found) { limit = found; break; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        CatalogLimits[cacheKey] = (stamp, limit);
        return limit;

        static long? Limit(JsonElement provider, string modelId) =>
            provider.ValueKind == JsonValueKind.Object && provider.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Object
                && models.TryGetProperty(modelId, out var model) && model.TryGetProperty("limit", out var limits) ? CodexContext.Integer(limits, "context") : null;
    }
}
