using Sic.Core.Terminals;

namespace Sic.Core.Agents;

public static class AgentLaunchBuilder
{
    public static TerminalLaunchRequest Build(string powerShell, AgentDefinition agent, string directory,
        string? resumeId = null, string? newSessionId = null, bool loadProfile = true, IReadOnlyList<string>? extraArguments = null)
    {
        if (resumeId is not null && newSessionId is not null)
            throw new ArgumentException("A launch cannot be both new and resumed.");
        if (resumeId is not null && !IsExactSessionId(agent.Id, resumeId))
            throw new ArgumentException("Resume requires the exact stored session ID, not a title, picker, or latest-session selector.", nameof(resumeId));
        if (newSessionId is not null && (agent.Id is not ("claude" or "opencode") || !IsExactSessionId(agent.Id, newSessionId)))
            throw new ArgumentException("Only Claude Code and OpenCode support assigning a new conversation ID through this adapter.", nameof(newSessionId));
        var arguments = new List<string>(agent.Arguments);
        // Options SIC adds of its own, as Claude Code's --settings for the usage-limit status line (ClaudeStatusLine).
        if (extraArguments is not null) arguments.AddRange(extraArguments);
        // A new Hermes session starts untitled, so Hermes names it and records its folder, and the pane finds it by that
        // folder and its start (HermesHistoryReader.FindCreated; docs/decisions/hermes-session-names.md).
        if (agent.Id == "hermes" && resumeId is null) arguments.AddRange(["chat", "--in", directory]);
        // OpenCode's --session continues that session, or creates it under that ID when it does not exist.
        if (newSessionId is not null) arguments.AddRange([agent.Id == "opencode" ? "--session" : "--session-id", newSessionId]);
        if (resumeId is not null)
        {
            arguments.AddRange(agent.Id switch
            {
                "hermes" => new[] { "--resume", resumeId, "--in", directory },
                "claude" => new[] { "--resume", resumeId },
                "codex" => new[] { "resume", resumeId, "--cd", directory },
                "opencode" => new[] { "--session", resumeId },
                _ => throw new ArgumentException("Unknown agent identity.", nameof(agent))
            });
        }
        // Codex otherwise runs the session in a shared app-server daemon it starts without a console, inside the pane's
        // process tree: every console program that daemon runs (git, the Windows-sandboxed pwsh for each command and for the
        // post-turn memory pass) gets a brand-new console, which Windows hands to the default terminal as a visible window, and
        // closing the pane kills the daemon under every other Codex session. In-process, they inherit the pane's own console.
        if (agent.Id == "codex" && !arguments.Contains("--no-daemon")) arguments.Add("--no-daemon");
        // OpenCode otherwise attaches to a background server that every OpenCode session shares, starting one if none runs
        // (docs/decisions/opencode-standalone.md): the agent's commands would run in that server instead of the pane, it would
        // use whichever history home it was started with, and one started from a pane dies with that pane's job.
        if (agent.Id == "opencode" && !arguments.Contains("--standalone")) arguments.Add("--standalone");
        var history = HistoryVariable(agent);
        var script = "$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath " + Literal(directory) +
            "; $env:TERMINAL_CWD = (Get-Location).ProviderPath; " + ScriptShimFallback(agent.Executable) + " " +
            (agent.Id == "opencode" ? OpenCodeVersionCheck(agent.Executable) + " " : "") +
            "& " + Literal(agent.Executable) + " " +
            string.Join(" ", arguments.Select(Literal)) + "; if ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }";
        var shellArguments = new List<string> { "-NoLogo" };
        if (!loadProfile) shellArguments.Add("-NoProfile");
        shellArguments.AddRange(["-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))]);
        return new TerminalLaunchRequest(powerShell, shellArguments, directory, Environment: new Dictionary<string, string?>
        {
            ["TERM"] = "xterm-256color", ["COLORTERM"] = "truecolor", ["TERM_PROGRAM"] = "SIC",
            ["TERMINAL_CWD"] = directory,
            [history.Key] = history.Value
        });
    }

    /// <summary>
    /// The environment variable, and its value, that points the agent at <see cref="AgentDefinition.HistoryDirectory"/>.
    /// OpenCode's is XDG_DATA_HOME, the folder above its own "opencode" data folder, so a history home of any other name
    /// cannot be expressed; it is refused rather than read from one place while OpenCode writes to another.
    /// Claude Code's default home, %USERPROFILE%\.claude, gets a null value: no CLAUDE_CONFIG_DIR at all. Set, even to that
    /// same folder, it makes Claude Code read its global state from %CLAUDE_CONFIG_DIR%\.claude.json instead of
    /// %USERPROFILE%\.claude.json, where every MCP server added at user scope is kept, so the pane would have none of them.
    /// </summary>
    public static KeyValuePair<string, string?> HistoryVariable(AgentDefinition agent)
    {
        switch (agent.Id)
        {
            case "hermes": return new("HERMES_HOME", agent.HistoryDirectory);
            // Null removes it from what the pane inherits too: a SIC started from an older SIC's pane has it set to the default.
            case "claude": return new("CLAUDE_CONFIG_DIR", IsClaudeDefaultHome(agent.HistoryDirectory) ? null : agent.HistoryDirectory);
            case "codex": return new("CODEX_HOME", agent.HistoryDirectory);
            case "opencode":
                var home = Path.TrimEndingDirectorySeparator(agent.HistoryDirectory);
                if (!Path.GetFileName(home).Equals("opencode", StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(home) is not { Length: > 0 } parent)
                    throw new InvalidDataException("OpenCode's history home must be a folder named opencode: OpenCode keeps its history in the opencode folder under XDG_DATA_HOME.");
                return new("XDG_DATA_HOME", parent);
            default: throw new ArgumentException("Unknown agent.", nameof(agent));
        }
    }

    /// <summary>Whether <paramref name="home"/> is %USERPROFILE%\.claude: full paths compared without a trailing separator, ignoring case.</summary>
    private static bool IsClaudeDefaultHome(string home)
    {
        static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return Path.IsPathFullyQualified(home) && string.Equals(Normal(home),
            Normal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExactSessionId(string agent, string id) => agent switch
    {
        // A timestamped id, a GUID, or the "local-" + SHA-256 id Hermes derives for a local session since its unified runtime
        // (hermes_state_keys.local_identity, seen October 20, 2026).
        "hermes" => System.Text.RegularExpressions.Regex.IsMatch(id, @"\A(?:[0-9]{8}_[0-9]{6}_[a-zA-Z0-9]{6,32}|local-[0-9a-f]{64})\z") || Guid.TryParseExact(id, "D", out _),
        "claude" or "codex" => Guid.TryParseExact(id, "D", out _),
        "opencode" => OpenCodeSessionIds.IsValid(id),
        _ => false
    };

    /// <summary>
    /// The PowerShell that runs before the agent, so an npm install starts where the execution policy refuses scripts. npm puts
    /// three shims for each tool on PATH: an extensionless one for sh, a .cmd and a .ps1, and PowerShell resolves the name to the
    /// .ps1. Windows PowerShell 5.1's default policy on Windows 11 is Restricted, so on a PC without PowerShell 7 the .ps1 is
    /// refused ("running scripts is disabled on this system"; seen on DOVE, November 26, 2026), where PowerShell 7 runs it under
    /// its own RemoteSigned. Under a policy that refuses unsigned scripts (Restricted, AllSigned, or Default and Undefined, which
    /// mean Restricted on Windows 11), or when the policy cannot be read, a name that resolves to a script whose .cmd or .exe is
    /// also on PATH gets an alias to that program: an alias comes before a script, so the agent's own invocation, and OpenCode's
    /// version check, run the program unchanged. The policy is left as it is, and a name already answered by an alias, a
    /// function or an application is left alone.
    /// </summary>
    public static string ScriptShimFallback(string executable) =>
        "$sicPolicy = ''; try { $sicPolicy = [string](Get-ExecutionPolicy) } catch { }; " +
        "if ($sicPolicy -in '','Restricted','AllSigned','Default','Undefined') { " +
        "$sicFound = Get-Command -Name " + Literal(executable) + " -ErrorAction SilentlyContinue | Select-Object -First 1; " +
        "if ($sicFound -is [System.Management.Automation.ExternalScriptInfo]) { " +
        "$sicProgram = Get-Command -Name " + Literal(executable) + " -CommandType Application -ErrorAction SilentlyContinue | " +
        "Where-Object { $_.Extension -and ($_.Extension -in ($env:PATHEXT -split ';')) } | Select-Object -First 1; " +
        "if ($null -ne $sicProgram) { Set-Alias -Name " + Literal(executable) + " -Value $sicProgram.Path } } };";

    /// <summary>
    /// The PowerShell that runs before OpenCode, so a version before 2.0 ends the pane with a plain explanation. Only 2.0 has
    /// <c>--standalone</c>: an older OpenCode rejects it, prints its whole help and stops, which reads as a hang. The npm
    /// package <c>opencode-ai</c> stays at 1.x; 2.0 is <c>@opencode/cli</c>. <c>opencode --version</c> prints "opencode v2.0.26"
    /// from 2.0 and "1.18.35" before it, so the first dotted number is the version; output with none is shown as it came.
    /// </summary>
    public static string OpenCodeVersionCheck(string executable) =>
        // Windows PowerShell 5.1 turns each stderr line of a native program redirected with 2>&1 into an error, which the
        // script's Stop makes terminating: the version is read under Continue, with any such line kept as its text alone.
        "$sicVersion = & { $ErrorActionPreference = 'Continue'; ((& " + Literal(executable) + " '--version' 2>&1 | " +
        "ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { $_ } }) | Out-String).Trim() }; " +
        "$sicFound = $sicVersion -match '(\\d+)\\.\\d+\\.\\d+'; if ($sicFound) { $sicVersion = $Matches[0] }; " +
        "if (-not $sicFound -or [int]$Matches[1] -lt 2) { " +
        "Write-Host ('SicTerm needs OpenCode 2.0 or later, and ' + " + Literal(executable) + " + ' (' + " +
        // An alias (ScriptShimFallback's) has no Path: its Definition is the program it runs.
        "$(foreach ($sicCommand in @(Get-Command -Name " + Literal(executable) + " -ErrorAction SilentlyContinue | Select-Object -First 1)) { " +
        "if ($sicCommand -is [System.Management.Automation.AliasInfo]) { $sicCommand.Definition } else { $sicCommand.Path } }) + ') reports: ' + $sicVersion); " +
        "Write-Host 'SicTerm starts OpenCode with --standalone, so its server runs in this pane; versions before 2.0 do not have it.'; " +
        "Write-Host 'OpenCode 2.0 is the npm package @opencode/cli, not opencode-ai: npm install -g --allow-scripts=@opencode/cli @opencode/cli@latest'; " +
        "exit 1 };";

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
