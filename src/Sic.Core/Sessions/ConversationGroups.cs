using Sic.Core.State;

namespace Sic.Core.Sessions;

public enum ConversationGroup { Pinned, Today, Yesterday, Earlier, Folder, Agent }

/// <summary>
/// One section of the conversation list and the conversations in it. <see cref="Key"/> is what a fold of the section is kept
/// under (<see cref="State.SectionFolds"/>): "pinned", "today", "yesterday", "earlier", "folder:" and the folder, or "agent:"
/// and the agent id. <see cref="Folder"/> is the folder a folder section stands for, normalized as pins are, or "" for the
/// conversations whose folder is unknown; null for every other kind.
/// </summary>
public sealed record ConversationSection(ConversationGroup Group, string Key, string Label, IReadOnlyList<Conversation> Conversations, string? Folder = null)
{
    public static readonly ConversationSection PinnedHeader = new(ConversationGroup.Pinned, "pinned", "Pinned", []);

    /// <summary>When the section's newest conversation was last written; MinValue for an empty section.</summary>
    public DateTimeOffset Newest => Conversations.Count == 0 ? DateTimeOffset.MinValue : Conversations.Max(conversation => conversation.UpdatedAt);
}

/// <summary>
/// The words for how the conversation list is grouped and sorted (<c>GroupBy</c> and <c>SortBy</c> in settings.json), as
/// <see cref="Appearance.MotionSetting"/> keeps its word. Grouped by folder by default: the repo a conversation ran in is what
/// a person looks for first (docs/decisions/sessions-groups.md).
/// </summary>
public static class ConversationListing
{
    public const string ByDate = "date", ByFolder = "folder", ByAgent = "agent";
    public const string Newest = "recent", ByName = "name";
    public static IReadOnlyList<string> Groupings { get; } = [ByDate, ByFolder, ByAgent];
    public static IReadOnlyList<string> Sorts { get; } = [Newest, ByName];

    public static string NormalizeGrouping(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        ByDate or "time" or "day" => ByDate,
        ByAgent => ByAgent,
        _ => ByFolder
    };

    public static string NormalizeSort(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        ByName or "title" or "alphabetical" => ByName,
        _ => Newest
    };
}

/// <summary>
/// Sorts the unfiltered conversation list into sections. A pinned conversation appears only under Pinned, whatever its date or
/// folder, and Pinned comes first whichever grouping is chosen. By date the sections are Today, Yesterday and Earlier: days
/// are calendar days in the given time zone, not 24-hour windows, so a conversation from 23:50 is "Yesterday" ten minutes
/// after midnight, the way a person reads it, and a time later than now (a clock that ran ahead on another machine) counts as
/// today rather than falling off the list. By folder each section is a folder: a conversation's own, rolled up to the nearest
/// pinned folder or workspace that holds it (<see cref="RootOf"/>), so a chat in docs\welcome sits under its repo; the
/// conversations whose folder nothing knows come last under "Folder unknown". By agent each section is an agent. Sections of
/// folders and agents are ordered newest first, or by name with the name sort, which also orders the conversations within
/// every section; otherwise those are newest first.
/// Since November 12, 2026 the folder grouping is the list by workspace (docs/decisions/sessions-groups.md): while folders are
/// listed (the roots), each section is a listed folder's workspace, in the list's order whatever the sort, and the conversations
/// outside every listed folder, the unknown folder's among them, come last as one section, Scratch, as the Workspaces panel has
/// them. With no folder listed the sections are as before. A ranked list keeps the conversations of each section in the order
/// given: a search's matches, as the catalog ranks them.
/// </summary>
public static class ConversationGroups
{
    public const string UnknownFolderLabel = "Folder unknown";

    public static IReadOnlyList<ConversationSection> Arrange(IEnumerable<Conversation> conversations, IReadOnlySet<string> pinned,
        DateTimeOffset now, TimeZoneInfo zone, string grouping = ConversationListing.ByDate, string sort = ConversationListing.Newest,
        Func<Conversation, string?>? folderOf = null, IReadOnlyCollection<string>? roots = null, Func<string, string>? agentName = null, bool ranked = false)
    {
        grouping = ConversationListing.NormalizeGrouping(grouping);
        var byName = ConversationListing.NormalizeSort(sort) == ConversationListing.ByName;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        folderOf ??= conversation => conversation.WorkingDirectory;
        agentName ??= id => id;
        var listed = (roots ?? []).Select(PinnedFolders.Normalize).Where(root => root.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int Listed(string? folder) => folder is { Length: > 0 } ? listed.FindIndex(root => string.Equals(root, folder, StringComparison.OrdinalIgnoreCase)) : -1;
        (ConversationGroup Group, string Key, string Label, string? Folder) SectionOf(Conversation conversation)
        {
            if (pinned.Contains(conversation.Key)) return (ConversationGroup.Pinned, "pinned", "Pinned", null);
            switch (grouping)
            {
                case ConversationListing.ByFolder:
                    var root = RootOf(folderOf(conversation), listed);
                    // With folders listed the sections are the workspaces: a conversation outside every listed folder is in Scratch.
                    if (listed.Count > 0 && Listed(root) < 0) return (ConversationGroup.Folder, "folder:", Workspaces.ScratchName, "");
                    return (ConversationGroup.Folder, "folder:" + root, root.Length == 0 ? UnknownFolderLabel : Leaf(root), root);
                case ConversationListing.ByAgent:
                    return (ConversationGroup.Agent, "agent:" + conversation.AgentId, agentName(conversation.AgentId), null);
                default:
                    var day = TimeZoneInfo.ConvertTime(conversation.UpdatedAt, zone).Date;
                    return day >= today ? (ConversationGroup.Today, "today", "Today", null)
                        : day == today.AddDays(-1) ? (ConversationGroup.Yesterday, "yesterday", "Yesterday", null)
                        : (ConversationGroup.Earlier, "earlier", "Earlier", null);
            }
        }
        IEnumerable<Conversation> Order(IEnumerable<Conversation> items) => ranked ? items : byName
            ? items.OrderBy(conversation => conversation.Title, StringComparer.OrdinalIgnoreCase).ThenBy(conversation => conversation.Key, StringComparer.Ordinal)
            : items.OrderByDescending(conversation => conversation.UpdatedAt).ThenBy(conversation => conversation.Key, StringComparer.Ordinal);
        var sections = conversations
            .DistinctBy(conversation => conversation.Key)
            .GroupBy(SectionOf)
            .Select(group => new ConversationSection(group.Key.Group, group.Key.Key, group.Key.Label, Order(group).ToList(), group.Key.Folder))
            .ToList();
        // Pinned first; the date sections in their order; the unknown folder last; the rest newest first or by name.
        return sections
            .OrderBy(section => section.Group == ConversationGroup.Pinned ? 0 : section.Folder is { Length: 0 } ? 2 : 1)
            .ThenBy(section => section.Group)
            // The workspaces in the Folders list's order, whatever the sort, as the Workspaces panel has them.
            .ThenBy(section => Math.Max(0, Listed(section.Folder)))
            .ThenBy(section => byName ? section.Label : "", StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(section => byName ? DateTimeOffset.MinValue : section.Newest)
            .ThenBy(section => section.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The folder a conversation's folder is listed under: the longest of <paramref name="roots"/> (the pinned folders and the
    /// workspaces) that is the folder or holds it, else the folder itself, normalized as pins are; "" for no folder.
    /// </summary>
    public static string RootOf(string? folder, IEnumerable<string> roots)
    {
        var own = PinnedFolders.Normalize(folder);
        if (own.Length == 0) return "";
        var best = own;
        var bestLength = -1;
        foreach (var candidate in roots)
        {
            var root = PinnedFolders.Normalize(candidate);
            if (root.Length == 0 || root.Length <= bestLength || !FolderMarks.IsUnder(root, own)) continue;
            best = root; bestLength = root.Length;
        }
        return best;
    }

    /// <summary>The part of a folder under its root, for a row's folder line: "docs\welcome" for a folder two levels inside its root, "" for the root itself or a folder not under it.</summary>
    public static string Under(string? folder, string root)
    {
        var own = PinnedFolders.Normalize(folder);
        if (root.Length == 0 || own.Length <= root.Length || !FolderMarks.IsUnder(root, own)) return "";
        return own[(root.Length + 1)..];
    }

    /// <summary>A folder's own name: the last segment of its path, or the whole path for a drive root.</summary>
    public static string Leaf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var leaf = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        return leaf.Length == 0 || leaf.EndsWith(':') ? path : leaf;
    }
}
