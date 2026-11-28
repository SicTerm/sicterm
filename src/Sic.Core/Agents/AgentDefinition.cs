using System.Text.Json.Serialization;

namespace Sic.Core.Agents;

/// <summary>
/// An agent SIC can start. <see cref="Enabled"/> is the user's switch in Settings: null while undecided, for an agent SIC
/// added since the settings were saved, until a detection finds it and turns it on (<see cref="BuiltInAgents.Decide"/>);
/// false only from the switch. A settings file written before the switch existed has no value, which reads as on: those
/// agents were in use.
/// <see cref="Icon"/> is the file the user chose for the agent's tile in Settings (an exe, an .ico or a .png), null for the
/// tool's own icon where SIC knows where its install keeps one (<see cref="AgentIcons.Source"/>).
/// </summary>
public sealed record AgentDefinition(string Id, string Name, string Executable, string[] Arguments, string HistoryDirectory, bool? Enabled = true, string? Icon = null)
{
    /// <summary>Whether the agent is offered and its history read: on, and not merely undecided.</summary>
    [JsonIgnore] public bool IsOn => Enabled == true;
}

public static class BuiltInAgents
{
    /// <summary>
    /// Every built-in agent, undecided: a first run turns on the ones detection finds. In alphabetical order, the order SicTerm
    /// lists them in everywhere (Chris, November 27, 2026).
    /// </summary>
    public static IReadOnlyList<AgentDefinition> Create(string userHome, string hermesHome) =>
    [
        new("claude", "Claude Code", "claude", [], Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(userHome, ".claude"), null),
        new("codex", "Codex", "codex", [], Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(userHome, ".codex"), null),
        new("hermes", "Hermes", "hermes", [], hermesHome, null),
        // OpenCode has no history variable of its own: its data folder is "opencode" under XDG_DATA_HOME, on Windows too.
        new("opencode", "OpenCode", "opencode", [], Path.Combine(
            Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(userHome, ".local", "share"), "opencode"), null)
    ];

    /// <summary>
    /// The built-in agents in <see cref="Create"/>'s order, each as saved where it was, then any other saved agent in its saved
    /// order. A settings file written before an agent was added offers it too, undecided, as Create makes it; Settings has no
    /// way to remove one, so a missing built-in was never chosen away. Settings has no way to reorder them either, so a file
    /// saved in an older order (Hermes first, before November 27, 2026) takes the current one with its settings kept.
    /// </summary>
    public static List<AgentDefinition> Merge(IEnumerable<AgentDefinition> saved, IEnumerable<AgentDefinition> builtIn)
    {
        var kept = saved.ToList();
        var builtIns = builtIn.ToList();
        var merged = builtIns.Select(agent => kept.FirstOrDefault(existing => existing.Id == agent.Id) ?? agent).ToList();
        merged.AddRange(kept.Where(existing => !builtIns.Any(agent => agent.Id == existing.Id)));
        return merged;
    }

    /// <summary>
    /// Turns on every undecided agent that <paramref name="detected"/> found. One not found stays undecided, so a later
    /// detection that finds it, after it is installed, turns it on (docs/decisions/agent-switches.md). Agents already on or
    /// off keep the user's choice whatever detection says. Null when nothing changed.
    /// </summary>
    public static List<AgentDefinition>? Decide(IEnumerable<AgentDefinition> agents, Func<string, bool?> detected)
    {
        var changed = false;
        var decided = agents.Select(agent =>
        {
            if (agent.Enabled is not null || detected(agent.Id) != true) return agent;
            changed = true;
            return agent with { Enabled = true };
        }).ToList();
        return changed ? decided : null;
    }

    /// <summary>
    /// Settings saved before October 19, 2026, when detection still switched off an agent it did not find: every agent that
    /// is off goes back to undecided, since an off of that time cannot be told from the user's own. Null when none was off.
    /// </summary>
    public static List<AgentDefinition>? ForgetDetectedOffs(IEnumerable<AgentDefinition> agents)
    {
        var changed = false;
        var forgotten = agents.Select(agent =>
        {
            if (agent.Enabled != false) return agent;
            changed = true;
            return agent with { Enabled = null };
        }).ToList();
        return changed ? forgotten : null;
    }
}
