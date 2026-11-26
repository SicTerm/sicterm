namespace Sic.Core.State;

/// <summary>
/// The folders worth pinning, found in the catalog's history: a folder counts when at least
/// <see cref="MinConversations"/> conversations of any agent ran in it within <see cref="Window"/>, most conversations first.
/// Choosing only adds after the existing pins and never reorders or removes one. Skipped: drive and share roots and the home
/// folder, where an agent started from a bare terminal lands; a folder inside one already pinned or chosen, so a repository is
/// pinned once and not again for each of its subfolders; a folder auto-pinned before and since unpinned, so a second run never
/// undoes the user's choice; and a folder that no longer exists. At most <see cref="MaxAdded"/> are added at a time.
/// </summary>
public static class AutoPins
{
    public const int MinConversations = 3;
    public const int MaxAdded = 8;
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    public sealed record Candidate(string Folder, int Conversations, DateTimeOffset LastUsed);

    /// <summary>
    /// The folders that pass the count within the window before <paramref name="now"/>, keyed as pins are keyed, most
    /// conversations first, then the most recently used. A folder written in two cases is one, spelled as most recently.
    /// A path that is not a full Windows path (a Linux one from WSL, a relative one) is left out.
    /// </summary>
    public static IReadOnlyList<Candidate> Rank(IEnumerable<(string? Folder, DateTimeOffset UpdatedAt)> activity, DateTimeOffset now)
    {
        var since = now - Window;
        return activity
            .Where(item => item.UpdatedAt >= since)
            .Select(item => (Folder: Key(item.Folder), item.UpdatedAt))
            .Where(item => item.Folder.Length > 0)
            .GroupBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= MinConversations)
            .Select(group =>
            {
                var latest = group.MaxBy(item => item.UpdatedAt);
                return new Candidate(latest.Folder, group.Count(), latest.UpdatedAt);
            })
            .OrderByDescending(item => item.Conversations).ThenByDescending(item => item.LastUsed)
            .ThenBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The folders to pin, in order, from <paramref name="ranked"/>: none already pinned or under a pin, none offered before
    /// (<paramref name="offered"/>, every folder an earlier run pinned), not the home folder or a root, and existing by
    /// <paramref name="exists"/>, which is asked only of a folder that passed everything else.
    /// </summary>
    public static List<string> Choose(IEnumerable<Candidate> ranked, IEnumerable<string> pinned, IEnumerable<string> offered,
        string? home, Func<string, bool> exists)
    {
        var pins = PinnedFolders.Clean(pinned);
        var before = PinnedFolders.Clean(offered);
        var chosen = new List<string>();
        foreach (var candidate in ranked)
        {
            if (chosen.Count == MaxAdded) break;
            var folder = PinnedFolders.Normalize(candidate.Folder);
            if (folder.Length == 0 || IsRoot(folder) || PinnedFolders.Same(folder, home) || PinnedFolders.Contains(before, folder)
                || pins.Concat(chosen).Any(pin => FolderMarks.IsUnder(pin, folder)) || !exists(folder)) continue;
            chosen.Add(folder);
        }
        return chosen;
    }

    /// <summary>A recorded working folder as pins key it, or "" when it is not a full Windows path.</summary>
    public static string Key(string? folder)
    {
        var path = (folder ?? "").Trim();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        var key = PinnedFolders.Normalize(path);
        try { return key.Length > 0 && Path.IsPathFullyQualified(key) ? key : ""; }
        catch (ArgumentException) { return ""; }
    }

    /// <summary>A drive root (C:\) or a share's root (\\server\share): where agents land, not a project.</summary>
    public static bool IsRoot(string folder)
    {
        var key = PinnedFolders.Normalize(folder);
        try { return string.Equals(PinnedFolders.Normalize(Path.GetPathRoot(key)), key, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return true; }
    }
}
