namespace Sic.Core.State;

/// <summary>
/// The sections folded down to their headers (<c>FoldedSections</c> in settings.json): the conversation list's groups by their
/// keys (<see cref="Sessions.ConversationSection.Key"/>), the folders of the Folders panel folded to their rows, their
/// trees hidden (<see cref="Root"/>), and the workspaces folded to their rows in the Workspaces panel (<see cref="Tabs"/>). A folder key is compared without case, as the folders are. A fold stays in the file while
/// its section is not shown (a folder with no conversations under the filter, say) and applies again when it is.
/// </summary>
public static class SectionFolds
{
    private const string RootPrefix = "root:";
    private const string TabsPrefix = "tabs:";
    /// <summary>The keys of sections that are gone: the Workplaces panel's two sections (until November 8, 2026) and the workspace rows' Folders sections (that day only); dropped on read.</summary>
    private static readonly string[] LegacyKeys = ["folders:pinned", "folders:workplaces", "folders:workspaces"];
    private static readonly string[] LegacyPrefixes = ["workspace:", "workspace-folders:"];

    /// <summary>
    /// The key a workspace's row in the Workspaces panel folds under, its sessions hidden: by the workspace's id, which is its
    /// folder as the list keeps it, or Scratch's. A row is open until its chevron folds it.
    /// </summary>
    public static string Tabs(string workspaceId) => TabsPrefix + PinnedFolders.Normalize(workspaceId);

    /// <summary>The key a folder of the Folders panel folds under: its row alone showing, its tree hidden.</summary>
    public static string Root(string folder) => RootPrefix + PinnedFolders.Normalize(folder);

    /// <summary>The saved set as it is kept: trimmed keys, nothing blank, compared without case; the keys of sections that no longer exist dropped.</summary>
    public static HashSet<string> Clean(IEnumerable<string>? folded)
    {
        var clean = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in folded ?? [])
            if (key?.Trim() is { Length: > 0 } valid && !LegacyKeys.Contains(valid, StringComparer.OrdinalIgnoreCase) && !LegacyPrefixes.Any(prefix => valid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                clean.Add(valid);
        return clean;
    }

    /// <summary>The set without the keys of folders no longer in the list, so a folder removed leaves nothing behind.</summary>
    public static HashSet<string> Prune(IEnumerable<string>? folded, IEnumerable<string> folders)
    {
        var kept = PinnedFolders.Clean(folders);
        var clean = Clean(folded);
        clean.RemoveWhere(key => key.StartsWith(RootPrefix, StringComparison.OrdinalIgnoreCase) && !PinnedFolders.Contains(kept, key[RootPrefix.Length..])
            || key.StartsWith(TabsPrefix, StringComparison.OrdinalIgnoreCase) && key[TabsPrefix.Length..] is var id && !PinnedFolders.Contains(kept, id) && !string.Equals(id, Workspaces.ScratchId, StringComparison.OrdinalIgnoreCase));
        return clean;
    }

    /// <summary>The set with the section folded or unfolded; the set given is not changed.</summary>
    public static HashSet<string> With(IEnumerable<string>? folded, string key, bool fold)
    {
        var clean = Clean(folded);
        if (fold) clean.Add(key); else clean.Remove(key);
        return clean;
    }
}
