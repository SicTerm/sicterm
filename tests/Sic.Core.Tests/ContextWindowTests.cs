using System.Text;
using Sic.Core.Limits;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ContextWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-context-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);
    private const string Id = "941efcc1-557b-4f8f-947a-b0fdb607d31c";

    public ContextWindowTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The status line's input as Claude Code documents it: the share it computes, and the usage it computes it from.</summary>
    private const string Input = """
        {"session_id":"__ID__","model":{"id":"claude-opus-4-5","display_name":"Opus 4.5"},"context_window":{"total_input_tokens":15234,"total_output_tokens":4521,"context_window_size":200000,"current_usage":{"input_tokens":100000,"output_tokens":1200,"cache_creation_input_tokens":20000,"cache_read_input_tokens":26000},"used_percentage":73,"remaining_percentage":27}}
        """;

    [Fact]
    public void TheReadingTakesTheShareTheTokensAndTheModel()
    {
        var reading = ContextWindow.FromClaudeStatusLine(Input.Replace("__ID__", Id), Now)!;
        Assert.Equal(new ContextReading(Id, 73, 200000, 146000, "Opus 4.5", Now), reading);
        Assert.Equal(ContextState.Filling, reading.State);
        Assert.Equal("73%", ContextWindow.Share(reading));
        Assert.Equal("Context window · 146k of 200k tokens · Opus 4.5", ContextWindow.Describe(reading));
    }

    [Fact]
    public void TheShareIsWorkedOutFromTheUsageWhenTheLineDoesNotGiveIt()
    {
        const string json = """{"session_id":"s","context_window":{"context_window_size":200000,"current_usage":{"input_tokens":150000,"cache_creation_input_tokens":0,"cache_read_input_tokens":40000}}}""";
        var reading = ContextWindow.FromClaudeStatusLine(json, Now)!;
        Assert.Equal(95, reading.UsedPercent);
        Assert.Equal(ContextState.Near, reading.State);
        Assert.Null(reading.Model);
        Assert.Equal("Context window · 190k of 200k tokens", ContextWindow.Describe(reading));
        // Output tokens are not in the window; a share given is taken over one worked out.
        Assert.Equal(12.5, ContextWindow.FromClaudeStatusLine("""{"session_id":"s","context_window":{"context_window_size":200000,"used_percentage":12.5,"current_usage":{"input_tokens":1,"output_tokens":199999}}}""", Now)!.UsedPercent);
    }

    [Fact]
    public void NothingBeforeTheFirstReplyOrWithoutASessionOrAWindow()
    {
        Assert.Null(ContextWindow.FromClaudeStatusLine("""{"session_id":"s","context_window":{"context_window_size":200000,"current_usage":null,"used_percentage":null}}""", Now));
        Assert.Null(ContextWindow.FromClaudeStatusLine("""{"session_id":"s","rate_limits":{}}""", Now));
        Assert.Null(ContextWindow.FromClaudeStatusLine("""{"context_window":{"used_percentage":5}}""", Now));
        Assert.Null(ContextWindow.FromClaudeStatusLine("not json", Now));
        Assert.Null(ContextWindow.FromClaudeStatusLine("""{"session_id":"s","context_window":{"context_window_size":0,"current_usage":{"input_tokens":5}}}""", Now));
    }

    [Theory]
    [InlineData(0, ContextState.Comfortable)]
    [InlineData(69.9, ContextState.Comfortable)]
    [InlineData(70, ContextState.Filling)]
    [InlineData(89.9, ContextState.Filling)]
    [InlineData(90, ContextState.Near)]
    [InlineData(100, ContextState.Near)]
    public void TheStateTurnsAtSeventyAndNinety(double used, ContextState expected) => Assert.Equal(expected, ContextWindow.State(used));

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(146000, "146k")]
    [InlineData(999499, "999k")]
    [InlineData(1200000, "1.2M")]
    public void TokensReadAsTheHeaderSaysThem(long tokens, string expected) => Assert.Equal(expected, ContextWindow.Tokens(tokens));

    [Fact]
    public void AReadingWithoutTokensDescribesItsShare()
    {
        var reading = new ContextReading(Id, 42.4, null, null, null, Now);
        Assert.Equal("42%", ContextWindow.Share(reading));
        Assert.Equal("Context window · 42% used", ContextWindow.Describe(reading));
        Assert.True(reading.HasContent);
        Assert.Equal(reading, ContextWindow.Deserialize(ContextWindow.Serialize(reading)));
        Assert.Null(ContextWindow.Deserialize("{\"SessionId\":"));
        Assert.Null(ContextWindow.Deserialize(""));
    }

    [Fact]
    public void AReadingOfTheModelAloneSaysSoAndOneWithNothingHasNoContent()
    {
        var modelOnly = new ContextReading(Id, null, null, null, "gpt-6.1-sol", Now);
        Assert.True(modelOnly.HasContent);
        Assert.Null(modelOnly.State);
        Assert.Equal("", ContextWindow.Share(modelOnly));
        Assert.Equal("Model · gpt-6.1-sol · context usage not reported", ContextWindow.Describe(modelOnly));
        Assert.False(new ContextReading(Id, null, null, null, " ", Now).HasContent);
        Assert.Equal(modelOnly, ContextWindow.Deserialize(ContextWindow.Serialize(modelOnly)));
    }

    [Fact]
    public void TheStatusLineKeepsEachSessionsReadingInItsOwnFileAndOnlyForAnExactId()
    {
        var data = Path.Combine(_root, "data");
        var calls = 0;
        ClaudeStatusLine.Handle(Input.Replace("__ID__", Id), data, Now, (_, _) => { calls++; return null; });
        var kept = ClaudeStatusLine.ReadContext(data, Id)!;
        Assert.Equal(73, kept.UsedPercent);
        Assert.Equal(Now, kept.ReadAt);
        Assert.Equal(Path.Combine(data, "limits", "context", Id + ".json"), ClaudeStatusLine.ContextFile(data, Id.ToUpperInvariant()));
        Assert.Null(ClaudeStatusLine.ContextFile(data, "smoke"));
        Assert.Null(ClaudeStatusLine.ContextSessionOf(Path.Combine(data, "limits", "context", Id + ".json.1234.tmp")));
        Assert.Equal(Id, ClaudeStatusLine.ContextSessionOf(Path.Combine(data, "limits", "context", Id + ".json")));
        // A session ID that is not a Claude Code one names no file, so nothing is written for it.
        ClaudeStatusLine.Handle(Input.Replace("__ID__", "smoke"), data, Now, (_, _) => null);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(data, "limits", "context")));
        Assert.Null(ClaudeStatusLine.ReadContext(data, "smoke"));
        // A later reading replaces the file; input with no context window leaves it.
        ClaudeStatusLine.Handle(Input.Replace("__ID__", Id).Replace("\"used_percentage\":73", "\"used_percentage\":91"), data, Now.AddMinutes(1), (_, _) => null);
        Assert.Equal(91, ClaudeStatusLine.ReadContext(data, Id)!.UsedPercent);
        ClaudeStatusLine.Handle("""{"session_id":"__ID__","workspace":{"current_dir":"C:\\nowhere"}}""".Replace("__ID__", Id), data, Now.AddMinutes(2), (_, _) => null);
        Assert.Equal(Now.AddMinutes(1), ClaudeStatusLine.ReadContext(data, Id)!.ReadAt);
        // The whole run, through the helper's entry point, keeps it too.
        var other = "01234567-89ab-4def-8123-456789abcdef";
        Assert.Equal(0, ClaudeStatusLine.Run(["--data-dir", data], new MemoryStream(Encoding.UTF8.GetBytes(Input.Replace("__ID__", other))), new MemoryStream(), Path.Combine(_root, "default")));
        Assert.Equal(73, ClaudeStatusLine.ReadContext(data, other)!.UsedPercent);
        // Pruning takes the files not written for the given time and leaves the rest.
        File.SetLastWriteTimeUtc(ClaudeStatusLine.ContextFile(data, other)!, DateTime.UtcNow.AddDays(-40));
        ClaudeStatusLine.PruneContext(data, TimeSpan.FromDays(30), DateTimeOffset.UtcNow);
        Assert.Null(ClaudeStatusLine.ReadContext(data, other));
        Assert.NotNull(ClaudeStatusLine.ReadContext(data, Id));
        ClaudeStatusLine.PruneContext(Path.Combine(_root, "nowhere"), TimeSpan.FromDays(30), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AReadingNamesThePaneAndTheClaudeCodeProcessItCameFrom()
    {
        var data = Path.Combine(_root, "data");
        ClaudeStatusLine.Handle(Input.Replace("__ID__", Id), data, Now, (_, _) => null, ("0123456789abcdef0123456789abcdef", 6788));
        var kept = ClaudeStatusLine.ReadContext(data, Id)!;
        Assert.Equal("0123456789abcdef0123456789abcdef", kept.Pane);
        Assert.Equal(6788, kept.AgentProcess);
        Assert.Equal(kept, ContextWindow.Deserialize(ContextWindow.Serialize(kept)));
        // Without an origin, as a status line outside SicTerm runs, neither is written, and a file kept before them reads back.
        ClaudeStatusLine.Handle(Input.Replace("__ID__", Id), data, Now, (_, _) => null);
        Assert.DoesNotContain("Pane", File.ReadAllText(ClaudeStatusLine.ContextFile(data, Id)!));
        Assert.Null(ContextWindow.Deserialize("""{"SessionId":"x","UsedPercent":9,"Model":"Opus 5.5","ReadAt":"2026-11-11T17:16:46+00:00"}""")!.Pane);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("pane-1_a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a b", false)]
    [InlineData(@"C:\x", false)]
    public void APaneIdIsShortAndPlain(string? id, bool expected) => Assert.Equal(expected, PaneOrigin.IsPaneId(id));

    [Fact]
    public void TheClaudeCodeProcessIsTheNearestAncestorThatIsNotAShell()
    {
        // The helper (40) under Git Bash's two bashes and a cmd, under claude.exe (6788), under the pane's pwsh, under SicTerm.
        var tree = new Dictionary<int, (int Parent, string Image)>
        {
            [40] = (30, "SicTermStatusLine.exe"), [30] = (20, "bash.exe"), [20] = (10, "BASH.EXE"), [10] = (6788, "cmd.exe"),
            [6788] = (8388, "claude.exe"), [8388] = (780, "pwsh.exe"), [780] = (4, "SicTerm.exe")
        };
        (int, string)? Of(int id) => tree.TryGetValue(id, out var entry) ? entry : null;
        Assert.Equal(6788, PaneOrigin.AgentProcess(40, Of));
        // Run straight from Claude Code (an npm install's node.exe), the parent is the process.
        tree[40] = (6789, "SicTermStatusLine.exe"); tree[6789] = (8388, "node.exe");
        Assert.Equal(6789, PaneOrigin.AgentProcess(40, Of));
        // A parent that has gone, or a line of shells to the top, names no process; nor does a loop.
        tree[40] = (99, "SicTermStatusLine.exe");
        Assert.Null(PaneOrigin.AgentProcess(40, Of));
        tree[40] = (30, "SicTermStatusLine.exe"); tree[10] = (0, "cmd.exe");
        Assert.Null(PaneOrigin.AgentProcess(40, Of));
        tree[10] = (30, "cmd.exe");
        Assert.Null(PaneOrigin.AgentProcess(40, Of));
    }

    [Fact]
    public void TheRunningProcessFindsItsAncestorInARealSnapshot()
    {
        var before = Environment.GetEnvironmentVariable(PaneOrigin.PaneVariable);
        try
        {
            Environment.SetEnvironmentVariable(PaneOrigin.PaneVariable, null);
            Assert.Equal((null, null), PaneOrigin.Current());
            Environment.SetEnvironmentVariable(PaneOrigin.PaneVariable, "0123456789abcdef0123456789abcdef");
            var (pane, process) = PaneOrigin.Current();
            Assert.Equal("0123456789abcdef0123456789abcdef", pane);
            // The test host's nearest ancestor that is not a shell is a running process, not the host itself.
            Assert.NotNull(process);
            Assert.NotEqual(Environment.ProcessId, process);
            using var ancestor = System.Diagnostics.Process.GetProcessById(process!.Value);
            Assert.False(ancestor.HasExited);
        }
        finally { Environment.SetEnvironmentVariable(PaneOrigin.PaneVariable, before); }
    }

    [Fact]
    public void APaneMovesOnlyWithItsOwnClaudeCodeProcessToAnotherSession()
    {
        const string other = "9c14d0fa-b144-4173-95fa-ba289fce7e27";
        var reading = new ContextReading(other, 64, 1_000_000, 635_821, "Fable 5.2", Now, "0123456789abcdef0123456789abcdef", 6788);
        // /clear in the pane: the process its session reported from goes on to another session.
        Assert.True(PaneOrigin.Moves(Id, 6788, reading));
        // The pane's session never reported, so no process is known: the first to report from the pane is taken.
        Assert.True(PaneOrigin.Moves(Id, null, reading));
        // Another Claude Code started from inside the pane (a window of its own) inherits the pane's ID but is not its process.
        Assert.False(PaneOrigin.Moves(Id, 1234, reading));
        // A reading of the pane's own session, or one with no process known, moves nothing.
        Assert.False(PaneOrigin.Moves(other.ToUpperInvariant(), 6788, reading));
        Assert.False(PaneOrigin.Moves(Id, 6788, reading with { AgentProcess = null }));
    }
}
