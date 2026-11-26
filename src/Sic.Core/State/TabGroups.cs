using Sic.Core.Appearance;

namespace Sic.Core.State;

/// <summary>
/// Tabs pinned whole (docs/decisions/tab-groups.md). A tab of two or more panes that is pinned is kept in settings.json as
/// <see cref="AppPreferences.TabGroups"/>, the tab as layout.json would save it (its panes, split tree, own name and color)
/// under the tab's own id, so Sessions can list it under Pinned and bring the whole tab back after it closes. A tab of one pane
/// pins its conversation instead, as it always did; the id is the same whether the tab is open or closed, so an open pinned tab
/// keeps its group in step with every save of the layout.
/// </summary>
public static class TabGroups
{
    /// <summary>Whether a tab is pinned as a group rather than through its one conversation: two or more panes.</summary>
    public static bool IsGroup(SavedTab tab) => tab.Panes is { Count: >= 2 };

    /// <summary>The saved groups as the file gave them, less any that cannot be a group (one pane or none, too many, no id, a pane without an id) and any second copy of an id.</summary>
    public static List<SavedTab> Clean(IEnumerable<SavedTab>? groups)
    {
        var clean = new List<SavedTab>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups ?? [])
        {
            if (group is null || string.IsNullOrWhiteSpace(group.Id) || group.Panes is null || group.Panes.Count < 2 || group.Panes.Count > PaneLayout.MaxPanes) continue;
            if (group.Panes.Any(pane => pane is null || string.IsNullOrWhiteSpace(pane.Id) || string.IsNullOrWhiteSpace(pane.AgentId))) continue;
            if (seen.Add(group.Id)) clean.Add(group);
        }
        return clean;
    }

    public static bool Contains(IEnumerable<SavedTab> groups, string id) => groups.Any(group => group.Id == id);

    public static SavedTab? Find(IEnumerable<SavedTab> groups, string id) => groups.FirstOrDefault(group => group.Id == id);

    /// <summary>The groups with <paramref name="tab"/> in place of the group of its id, or added at the end when none has it.</summary>
    public static List<SavedTab> Put(IEnumerable<SavedTab> groups, SavedTab tab)
    {
        var result = groups.ToList();
        var index = result.FindIndex(group => group.Id == tab.Id);
        if (index < 0) result.Add(tab); else result[index] = tab;
        return result;
    }

    public static List<SavedTab> Remove(IEnumerable<SavedTab> groups, string id) => groups.Where(group => group.Id != id).ToList();

    /// <summary>
    /// Whether two saved tabs would restore the same tab: same id, name, color, panes and tree. A record with a list in it is
    /// not equal to its copy, so the panes are compared one by one; the tree is a record of records and compares by value.
    /// </summary>
    public static bool Same(SavedTab a, SavedTab b) =>
        a.Id == b.Id && a.Title == b.Title && a.CustomTitle == b.CustomTitle && a.Color == b.Color
        && a.Panes.Count == b.Panes.Count && a.Panes.Zip(b.Panes).All(pair => pair.First == pair.Second)
        && Equals(a.Layout, b.Layout);

    /// <summary>What a group's row says under its name: the number of panes, then the folder its first pane works in.</summary>
    public static string Summary(SavedTab group)
    {
        var folder = group.Panes.Count > 0 && group.Panes[0].WorkingDirectory is { Length: > 0 } directory ? Leaf(directory) : null;
        return group.Panes.Count + " panes" + (folder is null ? "" : " · " + folder);
    }

    /// <summary>The folder's own name; a drive root is its drive.</summary>
    private static string Leaf(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    /// <summary>The group as a notice names it: its name, then the number of panes.</summary>
    public static string Label(SavedTab group, IReadOnlyDictionary<string, string> names) => TabLabels.TabTitle(group, names) + " · " + group.Panes.Count + " panes";

    /// <summary>Whether a search for <paramref name="query"/> lists the group: its own name or any pane's name contains the words.</summary>
    public static bool Matches(SavedTab group, string query, IReadOnlyDictionary<string, string> names)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return true;
        return TabLabels.TabTitle(group, names).Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || group.Panes.Any(pane => TabLabels.PaneTitle(pane, names).Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the group is listed under an agent filter and a folder scope: it holds a pane of that agent, and a pane working in
    /// the scope folder or under it. Null means no filter or no scope.
    /// </summary>
    public static bool Shown(SavedTab group, string? agentFilter, string? scopeFolder, IReadOnlyCollection<string> hiddenAgents)
    {
        if (agentFilter is not null && !group.Panes.Any(pane => pane.AgentId == agentFilter)) return false;
        if (scopeFolder is not null && !group.Panes.Any(pane => pane.WorkingDirectory.Length > 0 && FolderMarks.IsUnder(scopeFolder, pane.WorkingDirectory))) return false;
        // A group of agents that are every one switched off is left out, as their conversations are; a shell pane keeps a group listed.
        return group.Panes.Any(pane => pane.AgentId == "shell" || !hiddenAgents.Contains(pane.AgentId));
    }

    /// <summary>Everything a group's row reads, for the list's signature: a change in any of it rebuilds the rows.</summary>
    public static string Signature(SavedTab group, IReadOnlyDictionary<string, string> names) =>
        group.Id + '\u001f' + TabLabels.TabTitle(group, names) + '\u001f' + group.Color + '\u001f'
        + string.Join('\u001e', group.Panes.Select(pane => pane.Id + ':' + pane.AgentId + ':' + pane.SessionId + ':' + TabLabels.PaneTitle(pane, names) + ':' + pane.WorkingDirectory));
}
