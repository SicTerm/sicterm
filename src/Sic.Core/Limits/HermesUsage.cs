using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sic.Core.Agents;

namespace Sic.Core.Limits;

/// <summary>
/// Hermes's own <c>hermes usage --provider &lt;provider&gt; --json</c> (docs/decisions/usage-limits.md), which Hermes fetches with
/// its own login so SIC sees no credential: the ChatGPT plan's windows for <see cref="ChatGptProvider"/>, and the Claude plan's
/// for <see cref="ClaudeProvider"/>, where Hermes reads Claude Code's own login and asks the same usage endpoint Claude Code's
/// status line reports from. Each ask is a request Hermes makes, about 3 seconds, so the window asks at most every few minutes.
/// </summary>
public static class HermesUsage
{
    /// <summary>Hermes's provider for the ChatGPT plan.</summary>
    public const string ChatGptProvider = "openai-codex";
    /// <summary>Hermes's provider for Claude: the Claude plan when Hermes has Claude Code's login, else an API key, which reports no windows.</summary>
    public const string ClaudeProvider = "anthropic";
    /// <summary>How long <c>hermes usage</c> may take: it starts Python and makes one request.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The windows in the command's document for <paramref name="provider"/>: the ChatGPT plan's are labelled "Session" and
    /// "Weekly", the Claude plan's "Current session" and "Current week" (its "Opus week" is not shown); each a <c>used_percent</c>
    /// and a <c>resets_at</c>, read as of its <c>fetched_at</c>. Null for another provider, an unavailable account (an API key,
    /// no login), or anything else.
    /// </summary>
    public static LimitReading? Parse(string json, string provider)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "provider") != provider || !root.TryGetProperty("windows", out var windows)
                || windows.ValueKind != JsonValueKind.Array) return null;
            if (!DateTimeOffset.TryParse(Text(root, "fetched_at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var fetched))
                return null;
            LimitWindow? shortWindow = null, longWindow = null;
            foreach (var window in windows.EnumerateArray())
            {
                if (window.ValueKind != JsonValueKind.Object || !window.TryGetProperty("used_percent", out var used) || !used.TryGetDouble(out var percent)
                    || !DateTimeOffset.TryParse(Text(window, "resets_at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var resets))
                    continue;
                switch (Text(window, "label"))
                {
                    case "Session" or "Current session": shortWindow ??= new LimitWindow(Math.Clamp(percent, 0, 100), resets, UsageLimits.FiveHours); break;
                    case "Weekly" or "Current week": longWindow ??= new LimitWindow(Math.Clamp(percent, 0, 100), resets, UsageLimits.SevenDays); break;
                }
            }
            return shortWindow is null && longWindow is null ? null : new LimitReading(shortWindow, longWindow, fetched);
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Runs the command for <paramref name="provider"/> with the pane's own Hermes and history home, and reads its windows.
    /// A <c>.cmd</c> shim is run through cmd.exe. Null when it cannot run, fails, or takes longer than <see cref="Timeout"/>.
    /// </summary>
    public static LimitReading? Run(AgentDefinition hermes, string executable, string provider)
    {
        var shim = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        // Hermes's installer puts hermes.exe beside hermes.cmd; the exe needs no cmd.exe between.
        if (shim && File.Exists(Path.ChangeExtension(executable, ".exe"))) { executable = Path.ChangeExtension(executable, ".exe"); shim = false; }
        var start = shim ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", executable } } : new ProcessStartInfo(executable);
        foreach (var argument in new[] { "usage", "--provider", provider, "--json" }) start.ArgumentList.Add(argument);
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        start.RedirectStandardInput = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        var home = AgentLaunchBuilder.HistoryVariable(hermes);
        start.Environment[home.Key] = home.Value;
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout)) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return null; }
            return process.ExitCode == 0 ? Parse(output.Result, provider) : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }
}
