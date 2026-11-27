using System.Text;
using Sic.Core.Agents;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AgentLaunchBuilderTests
{
    [Fact]
    public void Build_NewHermesStartsUntitledInItsFolderSoHermesNamesIt()
    {
        var agent = new AgentDefinition("hermes", "Hermes", "hermes", [], Path.GetTempPath());
        var folder = Path.GetTempPath();
        var request = AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, folder);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(request.Arguments.Last()));
        Assert.Contains("& 'hermes' 'chat' '--in' '" + folder + "';", script);
        Assert.DoesNotContain("--continue", script);
        Assert.DoesNotContain("--create-if-missing", script);
        Assert.Equal(agent.HistoryDirectory, request.Environment!["HERMES_HOME"]);
        // A resume names its session, and nothing that would begin another.
        var id = "local-" + new string('a', 64);
        var resumed = Encoding.Unicode.GetString(Convert.FromBase64String(AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, folder, resumeId: id).Arguments.Last()));
        Assert.Contains("& 'hermes' '--resume' '" + id + "' '--in' '" + folder + "';", resumed);
        Assert.DoesNotContain("'chat'", resumed);
    }

    [Theory]
    [InlineData("hermes", "latest")]
    [InlineData("hermes", "--continue")]
    [InlineData("hermes", "local-d791796d")]
    [InlineData("hermes", "local-D791796DCB4C33258A1698DD8481FF439ACC50871F1078A85939DBE2310E8854")]
    [InlineData("claude", "not-a-uuid")]
    [InlineData("codex", "0123456789ab4def8123456789abcdef")]
    [InlineData("opencode", "ses_custom")]
    [InlineData("opencode", "01234567-89ab-4def-8123-456789abcdef")]
    public void Build_RejectsAnInexactOrMalformedSessionId(string agentId, string id)
    {
        var agent = new AgentDefinition(agentId, agentId, agentId, [], Path.GetTempPath());
        Assert.Throws<ArgumentException>(() => AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, Path.GetTempPath(), resumeId: id));
    }

    [Fact]
    public void Build_NewClaudeConversationReceivesItsOwnIdentityAndHistoryHome()
    {
        const string id = "01234567-89ab-4def-8123-456789abcdef";
        var agent = new AgentDefinition("claude", "Claude Code", "claude", ["--verbose"], @"C:\history\claude");
        var request = AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, Path.GetTempPath(), newSessionId: id);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(request.Arguments.Last()));
        Assert.Contains("'--session-id' '" + id + "'", script);
        Assert.Equal(agent.HistoryDirectory, request.Environment!["CLAUDE_CONFIG_DIR"]);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("trailing separator")]
    [InlineData("other case")]
    [InlineData("forward slashes")]
    public void Build_ClaudeAtItsDefaultHomeRunsWithoutClaudeConfigDir(string form)
    {
        // Set, even to the default, CLAUDE_CONFIG_DIR moves Claude Code's global state, and its user-scope MCP servers with it.
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        home = form switch
        {
            "trailing separator" => home + Path.DirectorySeparatorChar,
            "other case" => home.ToUpperInvariant(),
            "forward slashes" => home.Replace('\\', '/') + "/",
            _ => home
        };
        var request = AgentLaunchBuilder.Build(@"C:\pwsh.exe", new AgentDefinition("claude", "Claude Code", "claude", [], home), Path.GetTempPath());
        Assert.True(request.Environment!.TryGetValue("CLAUDE_CONFIG_DIR", out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData(".claude-work")]
    [InlineData(@".claude\isolated")]
    public void Build_ClaudeAtAnyOtherHomeIsPointedAtIt(string underProfile)
    {
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), underProfile);
        var request = AgentLaunchBuilder.Build(@"C:\pwsh.exe", new AgentDefinition("claude", "Claude Code", "claude", [], home), Path.GetTempPath());
        Assert.Equal(home, request.Environment!["CLAUDE_CONFIG_DIR"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("01234567-89ab-4def-8123-456789abcdef")]
    public void Build_CodexRunsInsideThePaneWithoutTheSharedDaemon(string? resumeId)
    {
        var agent = new AgentDefinition("codex", "Codex", "codex", [], Path.GetTempPath());
        var request = AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, Path.GetTempPath(), resumeId: resumeId);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(request.Arguments.Last()));
        Assert.EndsWith("'--no-daemon'; if ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }", script);
        if (resumeId is not null) Assert.Contains("'resume' '" + resumeId + "' '--cd'", script);
        Assert.Equal(agent.HistoryDirectory, request.Environment!["CODEX_HOME"]);
        var explicitly = AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent with { Arguments = ["--no-daemon"] }, Path.GetTempPath(), resumeId: resumeId);
        var once = Encoding.Unicode.GetString(Convert.FromBase64String(explicitly.Arguments.Last()));
        Assert.Equal(1, once.Split("'--no-daemon'").Length - 1);
        var claude = AgentLaunchBuilder.Build(@"C:\pwsh.exe", new AgentDefinition("claude", "Claude", "claude", [], Path.GetTempPath()), Path.GetTempPath());
        Assert.DoesNotContain("--no-daemon", Encoding.Unicode.GetString(Convert.FromBase64String(claude.Arguments.Last())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ses_ef18561f8ffeGZqmbqg6IknrnW")]
    public void Build_OpenCodeRunsItsOwnServerAndKeepsHistoryInTheChosenHome(string? resumeId)
    {
        var agent = new AgentDefinition("opencode", "OpenCode", "opencode", [], @"C:\history\share\opencode\");
        var newId = resumeId is null ? OpenCodeSessionIds.New(DateTimeOffset.UtcNow) : null;
        var request = AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, Path.GetTempPath(), resumeId: resumeId, newSessionId: newId);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(request.Arguments.Last()));
        Assert.Contains("'--session' '" + (resumeId ?? newId) + "'", script);
        Assert.EndsWith("'--standalone'; if ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }", script);
        Assert.Equal(@"C:\history\share", request.Environment!["XDG_DATA_HOME"]);
        var explicitly = AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent with { Arguments = ["--standalone"] }, Path.GetTempPath(), resumeId: resumeId, newSessionId: newId);
        Assert.Equal(1, Encoding.Unicode.GetString(Convert.FromBase64String(explicitly.Arguments.Last())).Split("'--standalone'").Length - 1);
        // The version check runs before OpenCode itself, in the pane's PowerShell, and only for OpenCode.
        Assert.Contains(AgentLaunchBuilder.OpenCodeVersionCheck("opencode") + " & 'opencode'", script);
        var claude = AgentLaunchBuilder.Build(@"C:\pwsh.exe", new AgentDefinition("claude", "Claude", "claude", [], Path.GetTempPath()), Path.GetTempPath());
        Assert.DoesNotContain("--version", Encoding.Unicode.GetString(Convert.FromBase64String(claude.Arguments.Last())));
    }

    private static string PowerShell => TestShells.PowerShell;

    /// <summary>
    /// The launch script, run in PowerShell against a stand-in opencode on PATH: a 1.x answers "--version" the way 1.18.35
    /// does and would print its help to anything else, and 2.0 answers the way 2.0.26 does and is then started with the
    /// session arguments. Stderr noise around the version is ignored, as the real command's would be.
    /// </summary>
    [Theory]
    [InlineData("1.18.35", false)]
    [InlineData("opencode v2.0.26", true)]
    public async Task Build_OpenCodeBefore2ExplainsItselfInThePaneInsteadOfStarting(string version, bool starts)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sic-opencode-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "opencode.ps1"),
                "if ($args -contains '--version') { [Console]::Error.WriteLine('noise'); '" + version + "'; exit 0 }\n" +
                "'STARTED ' + ($args -join ' '); exit 7\n");
            var agent = new AgentDefinition("opencode", "OpenCode", "opencode", [], Path.Combine(folder, "share", "opencode"));
            var request = AgentLaunchBuilder.Build(PowerShell, agent, folder, newSessionId: OpenCodeSessionIds.New(DateTimeOffset.UtcNow), loadProfile: false);
            var start = new System.Diagnostics.ProcessStartInfo(request.Executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = request.WorkingDirectory
            };
            // The stand-in is a script: run where scripts may, so the test is the version check in either PowerShell
            // (Build_AnNpmInstallStartsWhereThePolicyRefusesScripts is the one about the policy).
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("RemoteSigned");
            foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
            start.Environment["PATH"] = folder + ";" + Environment.GetEnvironmentVariable("PATH");
            start.Environment.Remove("PSModulePath");
            foreach (var (key, value) in request.Environment!) if (value is not null) start.Environment[key] = value;
            using var process = System.Diagnostics.Process.Start(start)!;
            // Read together: a long error on stderr would otherwise fill its pipe while stdout is read to its end.
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            await errors;
            Assert.True(process.WaitForExit(30_000), "PowerShell did not finish.");
            if (starts)
            {
                Assert.Contains("STARTED --session ses_", output);
                Assert.Contains("--standalone", output);
                Assert.DoesNotContain("SicTerm needs OpenCode", output);
                Assert.Equal(7, process.ExitCode);
            }
            else
            {
                Assert.Contains("SicTerm needs OpenCode 2.0 or later, and opencode (" + Path.Combine(folder, "opencode.ps1") + ") reports: 1.18.35", output);
                Assert.Contains("@opencode/cli", output);
                Assert.DoesNotContain("STARTED", output);
                Assert.Equal(1, process.ExitCode);
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static string WindowsPowerShell => TestShells.WindowsPowerShell;

    /// <summary>
    /// The launch script against npm's three shims for a tool (sh, .cmd and .ps1, stand-ins here), in PowerShell 7 and in Windows
    /// PowerShell 5.1, which a PC without PowerShell 7 runs every pane in. Where the execution policy allows scripts the .ps1
    /// runs, as PowerShell resolves the name; under Restricted, Windows 11's default for 5.1, the .cmd runs instead of the
    /// refusal. The .cmd writes to stderr around its version, as a native program can, through the version check's 2>&amp;1.
    /// </summary>
    [Theory]
    [InlineData("7", "RemoteSigned", "PS1", "opencode v2.0.26")]
    [InlineData("7", "Restricted", "CMD", "opencode v2.0.26")]
    [InlineData("5.1", "RemoteSigned", "PS1", "opencode v2.0.26")]
    [InlineData("5.1", "Restricted", "CMD", "opencode v2.0.26")]
    [InlineData("5.1", "Restricted", "CMD", "1.18.35")]
    public async Task Build_AnNpmInstallStartsWhereThePolicyRefusesScripts(string version, string policy, string runs, string answer)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sic-shims-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "opencode"), "#!/bin/sh\necho SH-STARTED\n");
            File.WriteAllText(Path.Combine(folder, "opencode.cmd"),
                "@echo off\r\nif \"%~1\"==\"--version\" (echo noise 1>&2& echo " + answer + "& exit /b 0)\r\necho CMD-STARTED %*\r\nexit /b 7\r\n");
            File.WriteAllText(Path.Combine(folder, "opencode.ps1"),
                "if ($args -contains '--version') { '" + answer + "'; exit 0 }\n'PS1-STARTED ' + ($args -join ' '); exit 7\n");
            var agent = new AgentDefinition("opencode", "OpenCode", "opencode", [], Path.Combine(folder, "share", "opencode"));
            var request = AgentLaunchBuilder.Build(version == "7" ? PowerShell : WindowsPowerShell, agent, folder,
                newSessionId: OpenCodeSessionIds.New(DateTimeOffset.UtcNow), loadProfile: false);
            var start = new System.Diagnostics.ProcessStartInfo(request.Executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = request.WorkingDirectory
            };
            // The policy for this process only, ahead of the script, as a machine's own would be.
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add(policy);
            foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
            start.Environment["PATH"] = folder + ";" + Environment.GetEnvironmentVariable("PATH");
            // Inherited from a PowerShell 7 that started the tests, its module path points 5.1 at 7's modules.
            start.Environment.Remove("PSModulePath");
            foreach (var (key, value) in request.Environment!) if (value is not null) start.Environment[key] = value;
            using var process = System.Diagnostics.Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(30_000), "PowerShell did not finish.");
            Assert.DoesNotContain("SH-STARTED", output);
            if (answer.StartsWith('1'))
            {
                // The explanation names the program that answered, the .cmd behind the alias.
                Assert.True(output.Contains("SicTerm needs OpenCode 2.0 or later, and opencode (" + Path.Combine(folder, "opencode." + runs.ToLowerInvariant()) + ") reports: 1.18.35",
                    StringComparison.Ordinal), "Output: " + output + "\nErrors: " + await errors);
                Assert.DoesNotContain("-STARTED", output);
                Assert.Equal(1, process.ExitCode);
                return;
            }
            Assert.True(output.Contains(runs + "-STARTED --session ses_", StringComparison.Ordinal), "Output: " + output + "\nErrors: " + await errors);
            Assert.Contains("--standalone", output);
            Assert.DoesNotContain("SicTerm needs OpenCode", output);
            Assert.Equal(7, process.ExitCode);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(@"C:\history\opencode-data")]
    [InlineData(@"C:\")]
    public void Build_RefusesAnOpenCodeHomeThatXdgDataHomeCannotName(string home)
    {
        var agent = new AgentDefinition("opencode", "OpenCode", "opencode", [], home);
        Assert.Throws<InvalidDataException>(() => AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, Path.GetTempPath()));
    }

    [Fact]
    public void Build_OnlyClaudeAndOpenCodeTakeANewSessionId()
    {
        var codex = new AgentDefinition("codex", "Codex", "codex", [], Path.GetTempPath());
        Assert.Throws<ArgumentException>(() => AgentLaunchBuilder.Build(@"C:\pwsh.exe", codex, Path.GetTempPath(), newSessionId: "01234567-89ab-4def-8123-456789abcdef"));
        var opencode = new AgentDefinition("opencode", "OpenCode", "opencode", [], @"C:\history\opencode");
        Assert.Throws<ArgumentException>(() => AgentLaunchBuilder.Build(@"C:\pwsh.exe", opencode, Path.GetTempPath(), newSessionId: "01234567-89ab-4def-8123-456789abcdef"));
    }

    [Fact]
    public void OpenCodeSessionIds_AreMadeAsOpenCodeMakesThemSoNewerSortsFirst()
    {
        // Recorded from OpenCode 2.0.23: ses_ef18561f8ffe… was made 8 ms before its time_created, with counter 1; SIC's counter is 0.
        var id = OpenCodeSessionIds.New(DateTimeOffset.FromUnixTimeMilliseconds(1791244279303));
        Assert.StartsWith("ses_ef18561f8fff", id);
        Assert.True(OpenCodeSessionIds.IsValid(id));
        var later = OpenCodeSessionIds.New(DateTimeOffset.FromUnixTimeMilliseconds(1791244279304));
        Assert.True(string.CompareOrdinal(later, id) < 0);
    }

    [Fact]
    public void BuiltInAgents_MergeAddsOnlyTheAgentsASavedListLacks()
    {
        var builtIn = BuiltInAgents.Create(@"C:\Users\someone", @"C:\hermes");
        AgentDefinition[] saved = [builtIn[1] with { Executable = "claude-custom" }, builtIn[0]];
        var merged = BuiltInAgents.Merge(saved, builtIn);
        Assert.Equal(["claude", "hermes", "codex", "opencode"], merged.Select(agent => agent.Id));
        Assert.Equal("claude-custom", merged[0].Executable);
        Assert.Equal("opencode", Path.GetFileName(merged[3].HistoryDirectory));
        Assert.All(builtIn, agent => Assert.Null(agent.Enabled));
        Assert.Null(merged[3].Enabled);
    }

    [Fact]
    public void BuiltInAgents_DecideTurnsOnOnlyUndecidedAgentsDetectionFound()
    {
        AgentDefinition Agent(string id, bool? enabled) => new(id, id, id, [], @"C:\h\" + id, enabled);
        AgentDefinition[] agents = [Agent("hermes", true), Agent("claude", false), Agent("codex", null), Agent("opencode", null), Agent("copilot", null)];
        var found = new Dictionary<string, bool> { ["hermes"] = false, ["claude"] = true, ["codex"] = true, ["opencode"] = false };
        var decided = BuiltInAgents.Decide(agents, id => found.TryGetValue(id, out var value) ? value : null)!;
        // OpenCode, not found, stays undecided rather than off: it is turned on once it is installed and found.
        Assert.Equal(new bool?[] { true, false, true, null, null }, decided.Select(agent => agent.Enabled));
        Assert.Equal(new[] { true, false, true, false, false }, decided.Select(agent => agent.IsOn));
        Assert.Null(BuiltInAgents.Decide(decided, id => found.TryGetValue(id, out var value) ? value : null));
        found["opencode"] = true;
        var installed = BuiltInAgents.Decide(decided, id => found.TryGetValue(id, out var value) ? value : null)!;
        Assert.Equal(new bool?[] { true, false, true, true, null }, installed.Select(agent => agent.Enabled));
    }

    [Fact]
    public void BuiltInAgents_ForgetDetectedOffsMakesOnlyOffAgentsUndecided()
    {
        AgentDefinition Agent(string id, bool? enabled) => new(id, id, id, [], @"C:\h\" + id, enabled);
        AgentDefinition[] agents = [Agent("hermes", true), Agent("claude", false), Agent("codex", null), Agent("opencode", false)];
        var forgotten = BuiltInAgents.ForgetDetectedOffs(agents)!;
        Assert.Equal(new bool?[] { true, null, null, null }, forgotten.Select(agent => agent.Enabled));
        Assert.Null(BuiltInAgents.ForgetDetectedOffs(forgotten));
    }

    [Fact]
    public void Build_RejectsConflictingNewAndResumeIdentities()
    {
        var agent = new AgentDefinition("claude", "Claude", "claude", [], Path.GetTempPath());
        const string id = "01234567-89ab-4def-8123-456789abcdef";
        Assert.Throws<ArgumentException>(() => AgentLaunchBuilder.Build(@"C:\pwsh.exe", agent, Path.GetTempPath(), id, id));
    }

    [Theory]
    [InlineData("hermes", "20260926_140330_dbff4d", "'--resume' '20260926_140330_dbff4d' '--in'")]
    [InlineData("hermes", "local-d791796dcb4c33258a1698dd8481ff439acc50871f1078a85939dbe2310e8854", "'--resume' 'local-d791796dcb4c33258a1698dd8481ff439acc50871f1078a85939dbe2310e8854' '--in'")]
    [InlineData("claude", "01234567-89ab-4def-8123-456789abcdef", "'--resume' '01234567-89ab-4def-8123-456789abcdef'")]
    [InlineData("codex", "01234567-89ab-4def-8123-456789abcdef", "'resume' '01234567-89ab-4def-8123-456789abcdef' '--cd'")]
    [InlineData("opencode", "ses_ef18561f8ffeGZqmbqg6IknrnW", "'--session' 'ses_ef18561f8ffeGZqmbqg6IknrnW'")]
    public void Build_ResumesExactIdentityInExactFolderWithoutBypassingApprovals(string agentId, string sessionId, string expected)
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "O'Brien; $(Write-Output BAD)"));
        var agent = new AgentDefinition(agentId, agentId, agentId, [], Path.Combine(Path.GetTempPath(), agentId));
        var result = AgentLaunchBuilder.Build(@"C:\Program Files\PowerShell\7\pwsh.exe", agent, folder, resumeId: sessionId);
        Assert.Equal(folder, result.WorkingDirectory);
        Assert.Contains("-EncodedCommand", result.Arguments);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(result.Arguments.Last()));
        Assert.Contains(expected, script);
        Assert.Contains("Set-Location -LiteralPath '" + folder.Replace("'", "''") + "'", script);
        Assert.DoesNotContain("--yolo", script);
        Assert.DoesNotContain("bypass", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--last", script);
        Assert.DoesNotContain("-NoProfile", result.Arguments);
    }
}
