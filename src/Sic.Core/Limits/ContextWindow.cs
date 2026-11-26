using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sic.Core.Limits;

/// <summary>How full a session's context window is (docs/decisions/context-window.md): room to spare, filling, or near the point where Claude Code compacts it.</summary>
public enum ContextState { Comfortable, Filling, Near }

/// <summary>
/// What a pane's header shows of its session's model and context window: the share of the window used (null when the agent
/// gives no way to know it, and the model stands alone), the window's size in tokens and the tokens in use when both are
/// known, the model's name when known, and when it was read. For Claude Code it is what the status line last reported; for the
/// other agents, what their own logs and databases hold (<see cref="ContextWindow"/>). A Claude Code reading also names the
/// pane and the Claude Code process it came from when the helper knows them (<see cref="PaneOrigin"/>), so a pane follows its
/// process to a new session.
/// </summary>
public sealed record ContextReading(string SessionId, double? UsedPercent, long? WindowTokens, long? UsedTokens, string? Model, DateTimeOffset ReadAt,
    string? Pane = null, int? AgentProcess = null)
{
    [JsonIgnore]
    public ContextState? State => UsedPercent is { } used ? ContextWindow.State(used) : null;
    /// <summary>Whether there is anything to show: a share, or at least a model.</summary>
    [JsonIgnore]
    public bool HasContent => UsedPercent is not null || !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// The context window of a Claude Code session, read from what Claude Code hands its status line command after each reply
/// (<c>context_window</c> beside <c>rate_limits</c>; see <see cref="ClaudeStatusLine"/>). Kept per session in the data folder,
/// since several Claude Code panes report at once, and shown in each pane's header so a long session is compacted before the
/// model does it unasked. Nothing here reads a credential or an agent's file.
/// </summary>
public static class ContextWindow
{
    /// <summary>From this share the window is filling: worth noticing, and a good moment to compact.</summary>
    public const double FillingPercent = 70;
    /// <summary>From this share Claude Code's own compaction is close.</summary>
    public const double NearPercent = 90;

    public static ContextState State(double usedPercent) =>
        usedPercent >= NearPercent ? ContextState.Near : usedPercent >= FillingPercent ? ContextState.Filling : ContextState.Comfortable;

    /// <summary>
    /// The reading in the status line's JSON, or null when there is none: <c>session_id</c> and <c>context_window</c> with
    /// <c>used_percentage</c> or, failing that, <c>current_usage</c> (input, cache creation and cache read tokens, the tokens the
    /// next request carries) over <c>context_window_size</c>. Before a session's first reply <c>current_usage</c> is null and
    /// there is no reading.
    /// </summary>
    public static ContextReading? FromClaudeStatusLine(string json, DateTimeOffset readAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("session_id", out var session) || session.ValueKind != JsonValueKind.String
                || session.GetString() is not { Length: > 0 } sessionId || !root.TryGetProperty("context_window", out var window) || window.ValueKind != JsonValueKind.Object)
                return null;
            var size = Integer(window, "context_window_size");
            long? used = null;
            if (window.TryGetProperty("current_usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                var parts = new[] { Integer(usage, "input_tokens"), Integer(usage, "cache_creation_input_tokens"), Integer(usage, "cache_read_input_tokens") };
                if (parts.Any(part => part is not null)) used = parts.Sum(part => part ?? 0);
            }
            var percent = Number(window, "used_percentage") ?? (used is { } tokens && size is > 0 ? 100.0 * tokens / size.Value : null);
            if (percent is null) return null;
            var model = root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.Object
                && modelElement.TryGetProperty("display_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            return new ContextReading(sessionId, Math.Clamp(percent.Value, 0, 100), size, used, model, readAt);
        }
        catch (JsonException) { return null; }
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    private static long? Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out var whole) ? whole : value.TryGetDouble(out var real) ? (long)real : null : null;

    private static readonly JsonSerializerOptions StoreOptions = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Serialize(ContextReading reading) => JsonSerializer.Serialize(reading, StoreOptions);

    /// <summary>A kept reading, or null for a missing, partial or malformed file.</summary>
    public static ContextReading? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ContextReading>(json, StoreOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>The share as the header shows it: a whole percentage; empty when the reading has none.</summary>
    public static string Share(ContextReading reading) =>
        reading.UsedPercent is { } used ? Math.Round(used).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%" : "";

    /// <summary>
    /// The words behind the meter, only what it does not show: the tokens in use of the window's size when both are known
    /// ("Context window · 146k of 200k tokens"), else the share ("Context window · 73% used"), and the model when named; for a
    /// reading of the model alone, the model ("Model · gpt-6.1-sol"), and that the agent reports no context usage.
    /// </summary>
    public static string Describe(ContextReading reading)
    {
        var model = string.IsNullOrWhiteSpace(reading.Model) ? "" : " · " + reading.Model;
        if (reading.UsedPercent is null)
            return reading.UsedTokens is { } inUse ? "Context window · " + Tokens(inUse) + " tokens in use, window size unknown" + model : "Model" + model + " · context usage not reported";
        var amount = reading is { UsedTokens: { } used, WindowTokens: > 0 and { } window } ? Tokens(used) + " of " + Tokens(window) + " tokens" : Share(reading) + " used";
        return "Context window · " + amount + model;
    }

    /// <summary>Tokens as the header says them: 126k, 1.2M, or the number under a thousand.</summary>
    public static string Tokens(long tokens) =>
        tokens >= 1_000_000 ? (tokens / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M"
        : tokens >= 1_000 ? Math.Round(tokens / 1_000.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + "k"
        : tokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
