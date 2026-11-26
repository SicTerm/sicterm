using System.Text.RegularExpressions;
using Sic.Core.State;

namespace Sic.Core.Appearance;

/// <summary>
/// What a tab and a pane are called. A pane's name is its conversation's local name when it has one, else the name given to
/// the pane itself, else the conversation's own title. A tab's name is its own once it has been given one; until then the tab
/// is called after its first pane, so a tab of one pane and that pane are always called the same. The tab strip and the pane
/// headers also drop the harness name from a title (new sessions are titled "Claude Code · repo"), since the agent tile beside
/// the name already says which harness runs there, and the width goes to the folder or purpose instead.
/// </summary>
public static partial class TabLabels
{
    /// <summary>The harness names as the app spells them in the titles it generates.</summary>
    public static IReadOnlyList<string> AgentNames { get; } = ["Claude Code", "Hermes", "Codex", "OpenCode", "PowerShell"];

    [GeneratedRegex(@"^(SIC|CommandCentral)-[a-f0-9]{32}$")]
    private static partial Regex CreationToken();

    /// <summary>The key a conversation's local name and color are kept under: agent and exact session ID.</summary>
    public static string Key(string agentId, string sessionId) => agentId + ":" + sessionId;

    /// <summary>
    /// A title as it is shown: the name SIC gave a new Hermes session until November 2026 (SIC- or CommandCentral- and hex) is
    /// a token, not a title. Hermes kept it as the session's title, since a title given at creation stops Hermes from naming
    /// the session; new sessions start untitled and Hermes names them (docs/decisions/hermes-session-names.md).
    /// </summary>
    public static string Display(string title) => CreationToken().IsMatch(title) ? "Untitled Hermes session" : title;

    /// <summary>The pane's name: the conversation's local name, the pane's own, or the conversation's title, in that order.</summary>
    public static string PaneTitle(SavedPane pane, IReadOnlyDictionary<string, string> names) =>
        Display(pane.SessionId is { } id && names.TryGetValue(Key(pane.AgentId, id), out var local) ? local : pane.CustomTitle ?? pane.Title);

    /// <summary>The tab's name: its own, or its first pane's.</summary>
    public static string TabTitle(SavedTab tab, IReadOnlyDictionary<string, string> names) =>
        tab.CustomTitle is { } own ? Display(own) : tab.Panes.Count > 0 ? PaneTitle(tab.Panes[0], names) : Display(tab.Title);

    /// <summary>Whether a name may be given: one line of 1 to 120 characters once trimmed.</summary>
    public static bool IsValidName(string name) => name.Trim() is { Length: >= 1 and <= 120 } trimmed && !trimmed.Any(char.IsControl);

    public static string WithoutAgentName(string title)
    {
        foreach (var name in AgentNames)
        {
            var prefix = name + " · ";
            if (title.StartsWith(prefix, StringComparison.Ordinal) && title.Length > prefix.Length) return title[prefix.Length..];
            if (title == "Untitled " + name + " session") return "Untitled session";
        }
        return title;
    }
}
