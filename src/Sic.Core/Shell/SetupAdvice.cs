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

/// <summary>The launch notice for <see cref="Gaps"/>: one sentence, beside a Set up button.</summary>
public sealed record SetupAdvice(SetupGap Gaps, string Message);

/// <summary>
/// The setup notice (November 27, 2026): one line at launch, with a Set up button that opens the Get set up panel
/// (<see cref="SetupCatalog"/>), shown at each launch until it is closed there and gone once the gap is not there. It began as a
/// longer message over five links to the vendors' pages; the panel took the links' place the same day.
/// </summary>
public static class SetupAdvisor
{
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

    /// <summary>The notice for <paramref name="gaps"/>, null for none.</summary>
    public static SetupAdvice? For(SetupGap gaps) => gaps switch
    {
        SetupGap.PowerShell => new(gaps, "PowerShell 7 was not found. SicTerm is running in Windows PowerShell 5.1, where some coding tools and commands fail."),
        SetupGap.Agent => new(gaps, "No coding agent was found."),
        SetupGap.Both => new(gaps, "PowerShell 7 and a coding agent were not found."),
        _ => null
    };
}

/// <summary>
/// One row of the Get set up panel: PowerShell 7 or a built-in agent. <paramref name="Line"/> sits under the name,
/// <paramref name="Detail"/> opens under "What is this?" (closed until clicked), and <paramref name="Page"/> is the vendor's own install page.
/// </summary>
public sealed record SetupItem(string Id, string Name, string Line, string Detail, Uri Page);

/// <summary>
/// The Get set up panel's rows and wording (approved by Chris, November 27, 2026), and the command each row's Install types
/// into a new PowerShell tab. SicTerm types the command and never runs it: the person presses Enter, answers what the installer
/// asks and accepts its terms themselves. Every command is the vendor's documented one, kept here and never fetched (the Store's
/// policy 10.2.2), and each installs for the account alone, without admin rights, going by the vendors' scripts as read that day.
/// </summary>
public static class SetupCatalog
{
    public const string PowerShell = "powershell";

    public const string Heading = "Get set up";

    /// <summary>
    /// The panel lists PowerShell 7 apart from the agents, each group under its own label: with one list of five under "only one
    /// is needed", PowerShell 7 read as a fifth choice of agent. A sentence under each label was taken out as more than the panel
    /// needs; each row keeps its "What is this?" paragraph, closed until clicked (Chris, November 27, 2026).
    /// </summary>
    public const string ShellLabel = "PowerShell";

    public const string AgentsLabel = "Coding agents · only one is needed";

    /// <summary>Under the rows, once for all of them.</summary>
    public const string Footnote = "Install opens a new PowerShell tab with the command. Press Enter to install.";

    /// <summary>Under the command in the install popup.</summary>
    public const string PopupFootnote = "Install opens a new PowerShell tab with this command. Press Enter to install.";

    /// <summary>Under Node.js's command in OpenCode's popup.</summary>
    public const string NodeFootnote = "Install Node.js opens a new PowerShell tab with this command. Press Enter to install. OpenCode can be installed once Node.js is found.";

    /// <summary>OpenCode's popup on a PC without npm.</summary>
    public const string OpenCodeNeedsNode = "Node.js is not installed. It installs for all users, so Windows asks for admin rights during setup.";

    public const string PowerShellInstalled = "PowerShell 7 is installed. New tabs run in it.";

    public const string NodeInstalled = "Node.js is installed. OpenCode can be installed now.";

    /// <summary>An agent's install found: the first one leaves the home panel behind, so the notice says where the rest are.</summary>
    public static string AgentInstalled(string name) => name + " is installed. More agents can be installed from Settings → Agents.";

    /// <summary>Under the PowerShell card's arguments in Settings → Agents.</summary>
    public const string ShellArgumentsNote = "For PowerShell tabs only. Agents start without them.";

    /// <summary>
    /// Node.js, for OpenCode, which has no Windows installer and installs through npm. Not a row of its own: OpenCode's popup leads
    /// with it where npm is not on Path. Its winget package is the MSI (wix), installed for all users, so Windows asks for admin
    /// rights as it starts; without winget, nodejs.org's download page.
    /// </summary>
    public static readonly SetupItem Node = new("node", "Node.js",
        "Needed by OpenCode, which installs through npm.",
        "Node.js installs for all users and needs admin rights; Windows asks for them when it starts.",
        new("https://nodejs.org/en/download"));

    /// <summary>The notice over a tab Install has just typed into.</summary>
    public const string Typed = "Press Enter to install.";

    /// <summary>Claude Code's row once its installer has run: the installer leaves its folder off Path (2.1.332, a Windows Sandbox run).</summary>
    public const string ClaudeOffPath = "Installed. Its folder is not on PATH, so claude works in SicTerm but not in other terminals.";

    public const string ClaudeOffPathNotice = "Claude Code is installed. Its folder is not on PATH.";

    public const string ClaudeOnPathNotice = "Added. New tabs and other terminals find claude.";

    /// <summary>PowerShell 7 first, then the agents in the order SicTerm lists them.</summary>
    public static readonly IReadOnlyList<SetupItem> Items =
    [
        new(PowerShell, "PowerShell 7",
            "Recommended. PowerShell 5.1 works, but many tools require 7.",
            "Windows comes with PowerShell 5.1, which dates from 2016. PowerShell 7 is the current version from Microsoft and installs beside it. " +
            "In 5.1, npm and some commands written for 7 fail.",
            new("https://learn.microsoft.com/powershell/scripting/install/install-powershell-on-windows")),
        new("claude", "Claude Code",
            "From Anthropic. Needs a paid Claude plan or a Console account.",
            "Anthropic's coding agent.",
            new("https://code.claude.com/docs/en/setup")),
        new("codex", "Codex",
            "From OpenAI. Needs a paid ChatGPT plan or an API key.",
            "OpenAI's coding agent.",
            new("https://developers.openai.com/codex/cli")),
        new("opencode", "OpenCode",
            "Open source. Works with most model providers. Needs Node.js.",
            "An open-source coding agent. It has no Windows installer, so it installs through npm, which comes with Node.js.",
            new("https://opencode.ai/docs")),
        new("hermes", "Hermes",
            "Open source, from Nous Research. Works with most model providers.",
            "An open-source agent from Nous Research. Its installer downloads Git, Python and the agent, which takes several minutes.",
            new("https://hermes-agent.nousresearch.com/docs/getting-started/installation"))
    ];

    public static SetupItem? Item(string id) => Items.FirstOrDefault(item => item.Id == id);

    /// <summary>
    /// The command Install types for <paramref name="id"/>, null where this PC has no way to run one and the row offers the
    /// install page instead. PowerShell 7 comes through winget (its MSIX since 7.6, per user), so it needs winget; OpenCode has
    /// no Windows installer, so it needs npm, named as npm.cmd because Windows PowerShell's default policy refuses npm.ps1.
    /// Codex's is OpenAI's documented line as it stands, child shell and all: run that way a fresh install worked in Windows
    /// PowerShell 5.1 under its default policy (a Windows Sandbox run, November 27, 2026).
    /// </summary>
    public static string? Command(string id, bool winget, bool npm) => id switch
    {
        PowerShell => winget ? "winget install --id Microsoft.PowerShell --source winget" : null,
        "claude" => "irm https://claude.ai/install.ps1 | iex",
        "codex" => "powershell -ExecutionPolicy ByPass -c \"irm https://chatgpt.com/codex/install.ps1 | iex\"",
        "opencode" => npm ? "npm.cmd install -g --allow-scripts=@opencode/cli @opencode/cli@latest" : null,
        "node" => winget ? "winget install --id OpenJS.NodeJS.LTS --source winget" : null,
        "hermes" => "iex (irm https://hermes-agent.nousresearch.com/install.ps1)",
        _ => null
    };

    /// <summary>
    /// Where an installer is known to put its program, for noticing an install that says nothing to Windows: Claude Code's
    /// under the profile's .local\bin (not added to Path), Codex's under Programs\OpenAI. Null for the others, which are found
    /// when their installers change Path.
    /// </summary>
    public static string? KnownProgram(string id, string userProfile, string localAppData) => id switch
    {
        "claude" => Path.Combine(ClaudeFolder(userProfile), "claude.exe"),
        "codex" => Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
        _ => null
    };

    /// <summary>The folder Claude Code's own installer puts claude.exe in.</summary>
    public static string ClaudeFolder(string userProfile) => Path.Combine(userProfile, ".local", "bin");
}
