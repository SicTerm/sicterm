using System.Text;
using System.Text.Json.Nodes;
using Sic.Core.Agents;
using Sic.Core.Limits;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ClaudeStatusLineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-statusline-tests-" + Guid.NewGuid().ToString("N"));
    public ClaudeStatusLineTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Input = """
        {"session_id":"s","workspace":{"current_dir":"__DIR__"},"rate_limits":{"five_hour":{"used_percentage":31,"resets_at":1791900000},"seven_day":{"used_percentage":22,"resets_at":1792300000}}}
        """;

    [Fact]
    public void TheCommandUsesForwardSlashesAndAShortPathForASpace()
    {
        Assert.Equal("C:/Users/chris/AppData/Local/SicTerm/current/SicTermStatusLine.exe",
            ClaudeStatusLine.Command(@"C:\Users\chris\AppData\Local\SicTerm\current\SicTermStatusLine.exe", null));
        Assert.Equal("C:/PROGRA~1/SIC/SicTermStatusLine.exe --data-dir C:/temp/smoke",
            ClaudeStatusLine.Command(@"C:\Program Files\SIC\SicTermStatusLine.exe", @"C:\temp\smoke", path => path.Replace("Program Files", "PROGRA~1")));
    }

    [Fact]
    public void ARunReadsItsInputKeepsTheReadingAndNamesItsDataFolder()
    {
        var data = Path.Combine(_root, "run-data");
        var input = new MemoryStream(Encoding.UTF8.GetBytes(Input.Replace("__DIR__", "C:\\\\nowhere")));
        var output = new MemoryStream();
        Assert.Equal(0, ClaudeStatusLine.Run(["--data-dir", data], input, output, Path.Combine(_root, "default")));
        Assert.Equal(31, ClaudeStatusLine.Read(data)!.Short!.UsedPercent);
        Assert.Null(ClaudeStatusLine.Read(Path.Combine(_root, "default")));
        Assert.Equal(0, ClaudeStatusLine.Run([], new MemoryStream(Encoding.UTF8.GetBytes("not json")), output, Path.Combine(_root, "default")));
        Assert.Null(ClaudeStatusLine.Read(Path.Combine(_root, "default")));
    }

    [Fact]
    public void TheSettingsKeepTheUsersStatusLineFieldsAndReplaceOnlyItsCommand()
    {
        var user = new JsonObject { ["type"] = "command", ["command"] = "~/.claude/line.sh", ["padding"] = 2, ["refreshInterval"] = 5 };
        var settings = JsonNode.Parse(ClaudeStatusLine.SettingsJson("C:/sic/SicTermStatusLine.exe", user))!["statusLine"]!;
        Assert.Equal("C:/sic/SicTermStatusLine.exe", (string?)settings["command"]);
        Assert.Equal(2, (int?)settings["padding"]);
        Assert.Equal(5, (int?)settings["refreshInterval"]);
        Assert.Equal("~/.claude/line.sh", (string?)user["command"]);
        Assert.Equal("""{"statusLine":{"type":"command","command":"x"}}""", ClaudeStatusLine.SettingsJson("x", null));
    }

    [Fact]
    public void SettingsFilesAreNamedByContentAndShared()
    {
        var a = ClaudeStatusLine.WriteSettings(_root, """{"statusLine":{"command":"a"}}""");
        Assert.Equal(a, ClaudeStatusLine.WriteSettings(_root, """{"statusLine":{"command":"a"}}"""));
        Assert.NotEqual(a, ClaudeStatusLine.WriteSettings(_root, """{"statusLine":{"command":"b"}}"""));
        Assert.Equal("""{"statusLine":{"command":"a"}}""", File.ReadAllText(a));
    }

    [Fact]
    public void TheUsersStatusLineIsTheLastOfUserProjectAndLocalAndNeverSicsOwn()
    {
        var home = Path.Combine(_root, "home"); var project = Path.Combine(_root, "project");
        Directory.CreateDirectory(home); Directory.CreateDirectory(Path.Combine(project, ".claude"));
        Assert.Null(ClaudeStatusLine.UserStatusLine(home, project));
        File.WriteAllText(Path.Combine(home, "settings.json"), "{ // comments are allowed\n  \"statusLine\": {\"type\":\"command\",\"command\":\"user\"}, }");
        Assert.Equal("user", (string?)ClaudeStatusLine.UserStatusLine(home, project)!["command"]);
        File.WriteAllText(Path.Combine(project, ".claude", "settings.json"), """{"statusLine":{"type":"command","command":"project"}}""");
        Assert.Equal("project", (string?)ClaudeStatusLine.UserStatusLine(home, project)!["command"]);
        Assert.Equal("user", (string?)ClaudeStatusLine.UserStatusLine(home, null)!["command"]);
        File.WriteAllText(Path.Combine(project, ".claude", "settings.local.json"), """{"statusLine":{"type":"command","command":"C:/sic/SicTermStatusLine.exe"}}""");
        Assert.Null(ClaudeStatusLine.UserStatusLine(home, project));
        // One copied from a SIC from before the rename to SicTerm is SIC's own too.
        File.WriteAllText(Path.Combine(project, ".claude", "settings.local.json"), """{"statusLine":{"type":"command","command":"C:/Users/me/AppData/Local/SIChat/current/SIChatStatusLine.exe"}}""");
        Assert.Null(ClaudeStatusLine.UserStatusLine(home, project));
        File.WriteAllText(Path.Combine(project, ".claude", "settings.local.json"), "{ broken");
        Assert.Equal("project", (string?)ClaudeStatusLine.UserStatusLine(home, project)!["command"]);
    }

    [Fact]
    public void HandlingKeepsTheReadingAndPrintsWhatTheUsersLinePrints()
    {
        var data = Path.Combine(_root, "data");
        var now = new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero);
        var calls = new List<(string Command, string Input)>();
        var input = Input.Replace("__DIR__", _root.Replace("\\", "\\\\"));
        // No line of the user's in this folder: nothing is printed, and the reading is kept.
        var printed = ClaudeStatusLine.Handle(input, data, now, (command, text) => { calls.Add((command, text)); return "user line"; });
        var kept = ClaudeStatusLine.Read(data)!;
        Assert.Equal(31, kept.Short!.UsedPercent);
        Assert.Equal(22, kept.Long!.UsedPercent);
        Assert.Equal(now, kept.ReadAt);
        // The home is the real one here, so only a project line is certain to be found.
        Directory.CreateDirectory(Path.Combine(_root, ".claude"));
        File.WriteAllText(Path.Combine(_root, ".claude", "settings.local.json"), """{"statusLine":{"type":"command","command":"echo hi"}}""");
        calls.Clear();
        printed = ClaudeStatusLine.Handle(input, data, now, (command, text) => { calls.Add((command, text)); return "user line"; });
        Assert.Equal("user line", printed);
        Assert.Equal(("echo hi", input), Assert.Single(calls));
        // Input without limits leaves the kept reading as it was.
        ClaudeStatusLine.Handle("""{"workspace":{"current_dir":"C:\\nowhere"}}""", data, now.AddHours(1), (_, _) => null);
        Assert.Equal(now, ClaudeStatusLine.Read(data)!.ReadAt);
    }

    [Theory]
    [InlineData("""{"workspace":{"current_dir":"C:\\a"},"cwd":"C:\\b"}""", @"C:\a")]
    [InlineData("""{"cwd":"C:\\b"}""", @"C:\b")]
    [InlineData("""{}""", null)]
    [InlineData("nonsense", null)]
    public void TheWorkingFolderComesFromTheInput(string input, string? expected) => Assert.Equal(expected, ClaudeStatusLine.WorkingDirectory(input));

    [Fact]
    public void ExtraArgumentsReachTheAgentsCommandLine()
    {
        var agent = new AgentDefinition("claude", "Claude Code", "claude", ["--verbose"], Path.GetTempPath());
        var request = AgentLaunchBuilder.Build("pwsh.exe", agent, _root, resumeId: "01234567-89ab-4def-8123-456789abcdef", extraArguments: ["--settings", @"C:\data\limits\claude-settings-1.json"]);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(request.Arguments.Last()));
        Assert.Contains("& 'claude' '--verbose' '--settings' 'C:\\data\\limits\\claude-settings-1.json' '--resume' '01234567-89ab-4def-8123-456789abcdef'", script);
    }
}
