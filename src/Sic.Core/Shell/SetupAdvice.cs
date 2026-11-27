namespace Sic.Core.Shell;

/// <summary>What a PC lacks for SicTerm as it is meant to be used: PowerShell 7, a coding agent, or both.</summary>
[Flags]
public enum SetupGap
{
    None = 0,
    /// <summary>No pwsh.exe: SicTerm runs in Windows PowerShell 5.1.</summary>
    PowerShell = 1,
    /// <summary>Detection found none of the built-in agents.</summary>
    Agent = 2,
    Both = PowerShell | Agent
}

/// <summary>A vendor's own install page.</summary>
public sealed record SetupLink(string Name, Uri Uri);

/// <summary>The message for <see cref="Gaps"/>, with the install pages it links to.</summary>
public sealed record SetupAdvice(SetupGap Gaps, string Message, IReadOnlyList<SetupLink> Links);

/// <summary>
/// The setup notice (November 27, 2026): shown at launch until it is closed there, and in Settings → Agents until it is closed
/// there, each for the gaps not yet closed in that place, and gone from both once the gap is not there. Links only, to each
/// vendor's install page: the Store policies (10.2.3) leave offering to install someone else's software to the reviewer, and
/// only PowerShell has an install for all users (its MSI, 7.6 and before), which its page covers beside the per-user ones.
/// </summary>
public static class SetupAdvisor
{
    public static readonly SetupLink PowerShell = new("PowerShell 7", new("https://learn.microsoft.com/powershell/scripting/install/install-powershell-on-windows"));

    /// <summary>The built-in agents' install pages, in the order SicTerm lists the agents.</summary>
    public static readonly IReadOnlyList<SetupLink> Agents =
    [
        new("Claude Code", new("https://code.claude.com/docs/en/setup")),
        new("Codex", new("https://developers.openai.com/codex/cli")),
        new("OpenCode", new("https://opencode.ai/docs")),
        new("Hermes", new("https://hermes-agent.nousresearch.com/docs/getting-started/installation"))
    ];

    /// <summary>Whether the shell SicTerm runs in is PowerShell 7: pwsh.exe, wherever it was found (an MSI's Program Files folder, or the MSIX's alias under WindowsApps).</summary>
    public static bool IsPowerShell7(string? shell) => shell is { Length: > 0 } && Path.GetFileName(shell).Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The gaps on this PC. <paramref name="agentFound"/> is null until detection has answered, or when it failed: no agent
    /// gap is claimed then, so a PC with agents is never told it has none.
    /// </summary>
    public static SetupGap Gaps(bool powerShell7, bool? agentFound) =>
        (powerShell7 ? SetupGap.None : SetupGap.PowerShell) | (agentFound == false ? SetupGap.Agent : SetupGap.None);

    /// <summary>The gaps still to show in one place: those on the PC that were not closed there.</summary>
    public static SetupGap Open(SetupGap gaps, SetupGap closed) => gaps & ~closed;

    /// <summary>The message for <paramref name="gaps"/>, null for none.</summary>
    public static SetupAdvice? For(SetupGap gaps) => gaps switch
    {
        SetupGap.PowerShell => new(gaps,
            "PowerShell 7 was not found, so SicTerm runs in Windows PowerShell 5.1. SicTerm works in 5.1, but PowerShell 7 or later is recommended.",
            [PowerShell]),
        SetupGap.Agent => new(gaps,
            "No coding agent was found. SicTerm works as a terminal on its own, but it is built around coding agents, and installing one is recommended.",
            Agents),
        SetupGap.Both => new(gaps,
            "PowerShell 7 and a coding agent were not found. SicTerm works as a terminal in Windows PowerShell 5.1. PowerShell 7 and a coding agent are recommended.",
            [PowerShell, .. Agents]),
        _ => null
    };
}
