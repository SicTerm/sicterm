namespace Sic.Core.State;

/// <summary>
/// Workspaces: groups of tabs, each with a tab strip of its own, one for each folder in the Folders panel and, while a session
/// runs outside every one of them, Scratch (docs/decisions/workspaces.md). Since November 8, 2026 nobody makes, names or moves
/// a workspace: a tab belongs to the workspace of the deepest folder in the list that holds its first pane's working folder
/// (<see cref="RootFor"/>), the workspace is named after that folder, and a layout read back is regrouped by the same rule
/// (<see cref="Regroup"/>), so the file keeps the tabs and the list keeps the folders.
/// </summary>
public static class Workspaces
{
    /// <summary>A bound for the folder list and the saved file, as <see cref="PaneLayout.MaxPanes"/> is for a tab; nothing legitimate reaches it.</summary>
    public const int MaxWorkspaces = 32;
    /// <summary>Tabs a workspace may hold, the bound the flat layout had.</summary>
    public const int MaxTabs = 64;
    /// <summary>The id and the name of the workspace for sessions outside every folder in the list; it exists while there is one, or while the list is empty.</summary>
    public const string ScratchId = "scratch";
    public const string ScratchName = "Scratch";

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>The id a folder's workspace has: the folder as the list keeps it (<see cref="PinnedFolders.Normalize"/>).</summary>
    public static string IdFor(string folder) => PinnedFolders.Normalize(folder);

    /// <summary>
    /// The saved workspaces as the file gives them, each cleaned, or, for a file from before there were workspaces, one workspace
    /// holding the file's tabs with its selected tab. Always one at least, and never more than <see cref="MaxWorkspaces"/>: the
    /// rest are refused as the other corruptions are, rather than dropped. With them, the id of the current one: the saved choice
    /// when it names a workspace in the list, else the first. What comes back is regrouped by <see cref="Regroup"/> before it is shown.
    /// </summary>
    public static (List<SavedWorkspace> Workspaces, string SelectedId) Read(WorkspaceLayout layout)
    {
        List<SavedWorkspace> workspaces;
        if (layout.Workspaces is null) workspaces = [new SavedWorkspace(NewId(), layout.Tabs ?? [], SelectedTabId: layout.SelectedTabId)];
        else
        {
            if (layout.Workspaces.Count > MaxWorkspaces) throw new InvalidDataException($"A layout holds up to {MaxWorkspaces} workspaces. It has not been changed.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            workspaces = [];
            foreach (var saved in layout.Workspaces)
            {
                if (saved is null || string.IsNullOrWhiteSpace(saved.Id) || !seen.Add(saved.Id)) throw new InvalidDataException("A saved workspace must have an id of its own. The layout has not been changed.");
                if (saved.Tabs is null || saved.Tabs.Count > MaxTabs) throw new InvalidDataException($"A saved workspace holds up to {MaxTabs} tabs. The layout has not been changed.");
                workspaces.Add(saved with { Home = NormalizeHome(saved.Home) });
            }
            if (workspaces.Count == 0) workspaces.Add(new SavedWorkspace(NewId(), []));
        }
        var selected = workspaces.FirstOrDefault(workspace => workspace.Id == layout.SelectedWorkspaceId) ?? workspaces[0];
        return (workspaces, selected.Id);
    }

    /// <summary>
    /// The folder in the list a working folder belongs to: the deepest one that is the folder or holds it (<see cref="FolderMarks.IsUnder"/>),
    /// so a folder listed inside another takes what is under it. Null for no folder, or one outside every folder in the list.
    /// </summary>
    public static string? RootFor(IEnumerable<string> folders, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        string? best = null;
        foreach (var candidate in PinnedFolders.Clean(folders))
            if (FolderMarks.IsUnder(candidate, folder) && (best is null || candidate.Length > best.Length)) best = candidate;
        return best;
    }

    /// <summary>
    /// Fills the folder list of a settings file from before November 8, 2026, once: the saved workspaces' home folders in their
    /// order, then the pinned folders and the Workplaces list (or the older Workspaces list) that an even older file kept, each
    /// folder once, so nothing set up before the rule is lost. The old lists are cleared, so the next save drops them. Stops at
    /// <see cref="MaxWorkspaces"/> folders. Returns whether anything changed.
    /// </summary>
    public static bool AdoptFolders(IReadOnlyList<SavedWorkspace> saved, AppPreferences preferences)
    {
        var legacy = (preferences.PinnedFolders ?? []).Concat(preferences.Workplaces ?? preferences.LegacyWorkspaces ?? []).ToList();
        var had = preferences.PinnedFolders is not null || preferences.Workplaces is not null || preferences.LegacyWorkspaces is not null;
        preferences.PinnedFolders = null; preferences.Workplaces = null; preferences.LegacyWorkspaces = null;
        if (preferences.Folders is not null && !had) return false;
        var homes = preferences.Folders is null ? saved.Select(workspace => workspace.Home).Where(home => home is not null).Cast<string>() : [];
        preferences.Folders = PinnedFolders.Clean((preferences.Folders ?? []).Concat(homes).Concat(legacy)).Take(MaxWorkspaces).ToList();
        return true;
    }

    /// <summary>
    /// The saved tabs grouped by the rule: one workspace per folder in the list, in the list's order, holding the tabs whose
    /// first pane works in the folder or under it and in no deeper listed folder, in their saved order; and Scratch, last,
    /// holding the tabs outside every folder (a tab without a folder among them), present while it holds one or the list is
    /// empty. Each workspace shows the first of its tabs that was showing in its saved workspace, else its first. The current
    /// workspace is where the saved current one's showing tab landed, else the workspace of its home, else the first.
    /// </summary>
    public static (List<SavedWorkspace> Workspaces, string SelectedId) Regroup(IReadOnlyList<SavedWorkspace> saved, string selectedId, IEnumerable<string> folders)
    {
        var roots = PinnedFolders.Clean(folders).Take(MaxWorkspaces).ToList();
        var tabs = new Dictionary<string, List<SavedTab>>(StringComparer.OrdinalIgnoreCase);
        var showing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots) tabs[IdFor(root)] = [];
        tabs[ScratchId] = [];
        string? selectedLanded = null;
        foreach (var workspace in saved)
            foreach (var tab in workspace.Tabs)
            {
                var id = RootFor(roots, FolderOf(tab)) is { } root ? IdFor(root) : ScratchId;
                tabs[id].Add(tab);
                if (tab.Id == workspace.SelectedTabId)
                {
                    showing.TryAdd(id, tab.Id);
                    if (workspace.Id == selectedId) selectedLanded = id;
                }
            }
        var result = roots.Select(root => new SavedWorkspace(IdFor(root), tabs[IdFor(root)], Home: root, SelectedTabId: showing.GetValueOrDefault(IdFor(root)) ?? tabs[IdFor(root)].FirstOrDefault()?.Id)).ToList();
        if (tabs[ScratchId].Count > 0 || roots.Count == 0) result.Add(new SavedWorkspace(ScratchId, tabs[ScratchId], SelectedTabId: showing.GetValueOrDefault(ScratchId) ?? tabs[ScratchId].FirstOrDefault()?.Id));
        var current = saved.FirstOrDefault(workspace => workspace.Id == selectedId);
        var selected = selectedLanded ?? (current?.Home is { } home && RootFor(roots, home) is { } under ? IdFor(under) : null);
        return (result, selected is not null && result.Any(workspace => workspace.Id == selected) ? selected : result[0].Id);
    }

    /// <summary>A folder as the list keeps it (<see cref="PinnedFolders.Normalize"/>), and null for blank.</summary>
    public static string? NormalizeHome(string? home) => PinnedFolders.Normalize(home) is { Length: > 0 } folder ? folder : null;

    /// <summary>What the workspace is called: its folder's own name, or Scratch for the workspace without one.</summary>
    public static string Name(SavedWorkspace workspace) => workspace.Home is { Length: > 0 } home ? Leaf(home) : ScratchName;

    /// <summary>The folder a tab counts as working in: its first pane's, the rule consolidation and grouping use; null for none.</summary>
    public static string? FolderOf(SavedTab? tab) => tab is { Panes.Count: > 0 } && tab.Panes[0].WorkingDirectory is { Length: > 0 } folder ? folder : null;

    /// <summary>The line under a workspace's name while its tabs are not listed: how many tabs, then its folder ("3 tabs · C:\repos\sic", "No tabs · C:\repos\sic"; "2 tabs" or "No tabs" for Scratch).</summary>
    public static string Summary(SavedWorkspace workspace)
    {
        var count = workspace.Tabs.Count;
        var tabs = count == 0 ? "No tabs" : count == 1 ? "1 tab" : count + " tabs";
        return workspace.Home is { Length: > 0 } folder ? tabs + " · " + folder : tabs;
    }

    /// <summary>The folder's own name; a drive root is its drive.</summary>
    public static string Leaf(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory.Replace('/', '\\'));
        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    /// <summary>The index a workspace's shortcut names, 1 to 9, for its place in the list; the last workspace for 9 when there are more.</summary>
    public static int IndexForDigit(int digit, int count) => count == 0 ? -1 : digit >= 9 ? count - 1 : Math.Min(digit - 1, count - 1);
}
