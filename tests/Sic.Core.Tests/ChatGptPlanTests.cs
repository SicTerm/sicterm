using Sic.Core.Limits;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ChatGptPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-chatgpt-tests-" + Guid.NewGuid().ToString("N"));
    public ChatGptPlanTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void HermesProviderIsTheModelBlocksProviderOnly()
    {
        Assert.Null(ChatGptPlan.HermesConfiguredProvider(_root));
        File.WriteAllText(Path.Combine(_root, "config.yaml"), """
            # Hermes
            provider: "top-level-is-not-it"
            model:
              # Inference provider selection:
              #   "openai-codex" - OpenAI Codex
              default: "gpt-6.1-sol-900k"

              provider: "openai-codex"   # chosen in hermes setup

              base_url: "https://chatgpt.com/backend-api/codex"
            toolsets:
              provider: "elsewhere"
            """);
        Assert.Equal("openai-codex", ChatGptPlan.HermesConfiguredProvider(_root));
        File.WriteAllText(Path.Combine(_root, "config.yaml"), "model:\n  provider: nous\n");
        Assert.Equal("nous", ChatGptPlan.HermesConfiguredProvider(_root));
        File.WriteAllText(Path.Combine(_root, "config.yaml"), "model:\n  default: x\nprovider: openai-codex\n");
        Assert.Null(ChatGptPlan.HermesConfiguredProvider(_root));
    }

    [Fact]
    public void HermesUsageGivesTheSessionAndWeeklyWindows()
    {
        // As printed by `hermes usage --provider openai-codex --json` on October 13, 2026, with a 5-hour window added.
        const string json = """
            {
              "provider": "openai-codex", "source": "usage_api", "title": "Account limits", "plan": "Prolite",
              "fetched_at": "2026-10-13T05:40:41.007093+00:00",
              "windows": [
                { "label": "Session", "used_percent": 12.5, "resets_at": "2026-10-13T09:00:00+00:00", "detail": null },
                { "label": "Weekly", "used_percent": 0.0, "resets_at": "2026-10-20T05:37:12+00:00", "detail": null }
              ],
              "details": ["You have 3 resets banked - use /usage reset to activate"], "unavailable_reason": null
            }
            """;
        var reading = HermesUsage.Parse(json, HermesUsage.ChatGptProvider)!;
        Assert.Equal(new LimitWindow(12.5, DateTimeOffset.Parse("2026-10-13T09:00:00Z"), UsageLimits.FiveHours), reading.Short);
        Assert.Equal(new LimitWindow(0, DateTimeOffset.Parse("2026-10-20T05:37:12Z"), UsageLimits.SevenDays), reading.Long);
        Assert.Equal(DateTimeOffset.Parse("2026-10-13T05:40:41.007093Z"), reading.ReadAt);
        // The document answers for the provider asked about and no other.
        Assert.Null(HermesUsage.Parse(json.Replace("openai-codex", "anthropic"), HermesUsage.ChatGptProvider));
        Assert.Null(HermesUsage.Parse(json, HermesUsage.ClaudeProvider));
        Assert.Null(HermesUsage.Parse("""{"provider":"openai-codex","fetched_at":"2026-10-13T05:40:41Z","windows":[],"unavailable_reason":"no credential"}""", HermesUsage.ChatGptProvider));
        Assert.Null(HermesUsage.Parse("No account usage available", HermesUsage.ChatGptProvider));
    }

    [Fact]
    public void HermesUsageGivesTheClaudePlansWindowsWithClaudeCodesLogin()
    {
        // As printed by `hermes usage --provider anthropic --json` on October 18, 2026, where Hermes reads Claude Code's own login
        // and asks the usage endpoint Claude Code's status line reports from; the Opus week is not shown.
        const string json = """
            {
              "provider": "anthropic", "source": "oauth_usage_api", "title": "Account limits", "plan": null,
              "fetched_at": "2026-10-18T17:40:26.579913+00:00",
              "windows": [
                { "label": "Current session", "used_percent": 26.0, "resets_at": "2026-10-18T20:09:59.541997+00:00", "detail": null },
                { "label": "Current week", "used_percent": 9.0, "resets_at": "2026-10-24T16:59:59.542015+00:00", "detail": null },
                { "label": "Opus week", "used_percent": 1.0, "resets_at": "2026-10-24T16:59:59.542015+00:00", "detail": null }
              ],
              "details": [], "unavailable_reason": null
            }
            """;
        var reading = HermesUsage.Parse(json, HermesUsage.ClaudeProvider)!;
        Assert.Equal(new LimitWindow(26, DateTimeOffset.Parse("2026-10-18T20:09:59.541997Z"), UsageLimits.FiveHours), reading.Short);
        Assert.Equal(new LimitWindow(9, DateTimeOffset.Parse("2026-10-24T16:59:59.542015Z"), UsageLimits.SevenDays), reading.Long);
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T17:40:26.579913Z"), reading.ReadAt);
        // An API key, or no login: Hermes prints no windows, and the pane draws on no plan.
        Assert.Null(HermesUsage.Parse("""{"provider":"anthropic","source":"oauth_usage_api","fetched_at":"2026-10-18T17:40:26Z","windows":[],"unavailable_reason":"No Anthropic OAuth token"}""", HermesUsage.ClaudeProvider));
    }

    [Fact]
    public void AKeptReadingRoundTrips()
    {
        Assert.Null(ChatGptPlan.Read(_root));
        var reading = new LimitReading(null, new LimitWindow(8, DateTimeOffset.Parse("2026-10-20T05:37:12Z"), UsageLimits.SevenDays), DateTimeOffset.Parse("2026-10-13T05:40:41Z"));
        ChatGptPlan.Write(_root, reading);
        Assert.Equal(reading, ChatGptPlan.Read(_root));
        // The Claude plan's file takes a reading of Hermes's as it takes the status line's, apart from the ChatGPT plan's.
        Assert.Null(ClaudeStatusLine.Read(_root));
        var claude = reading with { Short = new LimitWindow(26, DateTimeOffset.Parse("2026-10-18T20:09:59Z"), UsageLimits.FiveHours) };
        ClaudeStatusLine.Write(_root, claude);
        Assert.Equal(claude, ClaudeStatusLine.Read(_root));
        Assert.Equal(reading, ChatGptPlan.Read(_root));
    }
}
