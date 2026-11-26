using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sic.Core.Limits;

/// <summary>How a plan's usage window stands against the time that has passed in it (docs/decisions/usage-limits.md).</summary>
public enum LimitState { Unknown, OnPace, Ahead, Near, Reached }

/// <summary>One plan window: the share used, from 0 to 100, when it resets, and how long the window is.</summary>
public sealed record LimitWindow(double UsedPercent, DateTimeOffset ResetsAt, TimeSpan Length)
{
    /// <summary>The share of the window's time that has passed at <paramref name="now"/>, from 0 to 100.</summary>
    public double ElapsedPercent(DateTimeOffset now) =>
        Length <= TimeSpan.Zero ? 0 : Math.Clamp(100 * (1 - (ResetsAt - now) / Length), 0, 100);

    /// <summary>The short label the status bar gives the window: 5h, 7d.</summary>
    [JsonIgnore]
    public string Label => Length.TotalHours < 24 ? Math.Round(Length.TotalHours) + "h" : Math.Round(Length.TotalDays) + "d";
}

/// <summary>An agent's last report of its plan's windows, and when it was read. A window the agent did not report is null.</summary>
public sealed record LimitReading(LimitWindow? Short, LimitWindow? Long, DateTimeOffset ReadAt)
{
    [JsonIgnore]
    public IEnumerable<LimitWindow> Windows => new[] { Short, Long }.OfType<LimitWindow>();
}

/// <summary>
/// The usage limits of the plans behind Claude Code and Codex, as the agents themselves report them, and the pace rule that
/// colours them. Nothing here reads a credential: Claude Code hands its status line command the windows after each reply
/// (<see cref="FromClaudeStatusLine"/>), and Codex writes them into its session log with each turn's token count
/// (<see cref="FromCodexRollout"/>).
/// </summary>
public static class UsageLimits
{
    /// <summary>Points of usage ahead of the time passed that still count as on pace, so a window's first minutes don't flash amber.</summary>
    public const double PaceTolerance = 10;
    /// <summary>The share used from which a window is near its limit, whatever the pace.</summary>
    public const double NearPercent = 90;
    public static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    public static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);
    /// <summary>The longest window that is shown as the short one; a longer one is the long one.</summary>
    private static readonly TimeSpan ShortestLong = TimeSpan.FromDays(1);

    /// <summary>
    /// Unknown with no window or once it has reset (the share used since is not known until the agent reports again); Reached at
    /// 100; Near from <see cref="NearPercent"/>; Ahead when more than <see cref="PaceTolerance"/> points ahead of the time passed;
    /// otherwise OnPace.
    /// </summary>
    public static LimitState State(LimitWindow? window, DateTimeOffset now)
    {
        if (window is null || now >= window.ResetsAt) return LimitState.Unknown;
        if (window.UsedPercent >= 100) return LimitState.Reached;
        if (window.UsedPercent >= NearPercent) return LimitState.Near;
        return window.UsedPercent - window.ElapsedPercent(now) > PaceTolerance ? LimitState.Ahead : LimitState.OnPace;
    }

    /// <summary>
    /// The windows in the JSON Claude Code hands its status line command on standard input: <c>rate_limits.five_hour</c> and
    /// <c>rate_limits.seven_day</c>, each a <c>used_percentage</c> and a <c>resets_at</c> in Unix seconds. Null when there are none,
    /// as before a session's first reply or on a plan without them.
    /// </summary>
    public static LimitReading? FromClaudeStatusLine(string json, DateTimeOffset readAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("rate_limits", out var limits)
                || limits.ValueKind != JsonValueKind.Object) return null;
            var shortWindow = ClaudeWindow(limits, "five_hour", FiveHours);
            var longWindow = ClaudeWindow(limits, "seven_day", SevenDays);
            return shortWindow is null && longWindow is null ? null : new LimitReading(shortWindow, longWindow, readAt);
        }
        catch (JsonException) { return null; }
    }

    private static LimitWindow? ClaudeWindow(JsonElement limits, string name, TimeSpan length) =>
        limits.TryGetProperty(name, out var window) && window.ValueKind == JsonValueKind.Object
        && Number(window, "used_percentage") is { } used && Number(window, "resets_at") is { } resets
            ? new LimitWindow(Math.Clamp(used, 0, 100), DateTimeOffset.FromUnixTimeSeconds((long)resets), length) : null;

    /// <summary>
    /// The windows of the last <c>token_count</c> record in a Codex session log that carries any: <c>rate_limits.primary</c> and
    /// <c>secondary</c>, each a <c>used_percent</c>, a <c>window_minutes</c> and a <c>resets_at</c> in Unix seconds (older versions
    /// wrote <c>resets_in_seconds</c>, counted from the record's time). A window of a day or less is the short one, and longer
    /// the long one, so a plan with a weekly window alone shows that one. Only the last <paramref name="tailBytes"/> of the file
    /// are read: a long session's log runs to megabytes, and the record wanted is near its end.
    /// </summary>
    public static LimitReading? FromCodexRollout(string path, int tailBytes = 256 * 1024)
    {
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - tailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
            // A tail that starts mid-line drops that partial line.
            if (start > 0 && text.IndexOf('\n') is var first and >= 0) text = text[(first + 1)..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        foreach (var line in text.Split('\n').Reverse())
        {
            if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
            if (FromCodexRecord(line) is { } reading) return reading;
        }
        return null;
    }

    /// <summary>The windows in one line of a Codex session log, or null when it is not a token count carrying any.</summary>
    public static LimitReading? FromCodexRecord(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object) return null;
            if (limits.TryGetProperty("limit_id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { } limit && limit != "codex") return null;
            var at = root.TryGetProperty("timestamp", out var stamp) && stamp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(stamp.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed : DateTimeOffset.UtcNow;
            LimitWindow? shortWindow = null, longWindow = null;
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (CodexWindow(limits, name, at) is not { } window) continue;
                if (window.Length <= ShortestLong) shortWindow ??= window; else longWindow ??= window;
            }
            return shortWindow is null && longWindow is null ? null : new LimitReading(shortWindow, longWindow, at);
        }
        catch (JsonException) { return null; }
    }

    private static LimitWindow? CodexWindow(JsonElement limits, string name, DateTimeOffset at)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) return null;
        if (Number(window, "used_percent") is not { } used || Number(window, "window_minutes") is not { } minutes || minutes <= 0) return null;
        var resets = Number(window, "resets_at") is { } epoch ? DateTimeOffset.FromUnixTimeSeconds((long)epoch)
            : Number(window, "resets_in_seconds") is { } seconds ? at.AddSeconds(seconds) : (DateTimeOffset?)null;
        return resets is null ? null : new LimitWindow(Math.Clamp(used, 0, 100), resets.Value, TimeSpan.FromMinutes(minutes));
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    /// <summary>
    /// The Codex session logs most recently written in <paramref name="codexHome"/>: the newest <paramref name="count"/> in the
    /// day folders of <paramref name="today"/> and the day before (<c>sessions/YYYY/MM/DD</c>, local dates, as Codex names them).
    /// The plan's windows are the account's, so the newest log of any session is as good as the pane's own. Only two day folders
    /// are listed, since a long history holds thousands of logs.
    /// </summary>
    public static IReadOnlyList<string> RecentCodexRollouts(string codexHome, DateTime today, int count = 3)
    {
        var files = new List<FileInfo>();
        foreach (var day in new[] { today.Date, today.Date.AddDays(-1) })
        {
            var folder = CodexDayFolder(codexHome, day);
            try { if (Directory.Exists(folder)) files.AddRange(new DirectoryInfo(folder).EnumerateFiles("rollout-*.jsonl")); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return files.OrderByDescending(file => file.LastWriteTimeUtc).Take(count).Select(file => file.FullName).ToList();
    }

    /// <summary>The folder Codex writes a day's rollouts to: <c>sessions/YYYY/MM/DD</c> under its home, by the local date.</summary>
    public static string CodexDayFolder(string codexHome, DateTime day) =>
        Path.Combine(codexHome, "sessions", day.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture), day.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), day.ToString("dd", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The later of two readings; a window one leaves out is not taken from the other, since they may be of different times.</summary>
    public static LimitReading? Latest(LimitReading? a, LimitReading? b) => a is null ? b : b is null ? a : b.ReadAt > a.ReadAt ? b : a;

    private static readonly JsonSerializerOptions StoreOptions = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>A reading as it is kept on disk between the status line command and the window.</summary>
    public static string Serialize(LimitReading reading) => JsonSerializer.Serialize(reading, StoreOptions);

    /// <summary>A kept reading, or null for a missing, partial or malformed file.</summary>
    public static LimitReading? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<LimitReading>(json, StoreOptions); }
        catch (JsonException) { return null; }
    }
}
