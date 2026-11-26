namespace Sic.Core.Shell;

/// <summary>
/// The variables Windows gives a new logon, from the registry (the PC's and the user's), brought into a SicTerm that started
/// before an installer changed them. Every terminal and every detection inherits SicTerm's own variables, so without this an
/// agent installed while SicTerm runs (Claude Code's installer adds its folder to the user's Path) is not found and does not
/// run until SicTerm is restarted from a fresh Explorer (docs/decisions/agent-switches.md).
/// </summary>
public static class PersistentEnvironment
{
    private const string PathName = "Path";

    /// <summary>
    /// The registry's variables as a logon combines them: the PC's, then the user's over them, except Path, which is the PC's
    /// followed by the user's.
    /// </summary>
    public static Dictionary<string, string> Combine(IReadOnlyDictionary<string, string> machine, IReadOnlyDictionary<string, string> user)
    {
        var combined = new Dictionary<string, string>(machine, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in user)
            combined[name] = string.Equals(name, PathName, StringComparison.OrdinalIgnoreCase) && combined.TryGetValue(name, out var system) && system.Length > 0
                ? system.TrimEnd(';') + ";" + value
                : value;
        return combined;
    }

    /// <summary>
    /// What to set in a process whose variables <paramref name="process"/> reads, given the registry's variables when last read
    /// (<paramref name="before"/>, null for the first read) and now (<paramref name="after"/>): each name with its new value, null
    /// to remove it. A variable follows the registry where the process has none, or has the value the registry had before; a
    /// value of the process's own, from whatever started it, is kept. Path is <see cref="MergePath"/>'s.
    /// </summary>
    public static Dictionary<string, string?> Changes(Func<string, string?> process, IReadOnlyDictionary<string, string>? before, IReadOnlyDictionary<string, string> after)
    {
        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var names = after.Keys.Concat(before?.Keys ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var current = process(name);
            var was = before?.GetValueOrDefault(name);
            var now = after.GetValueOrDefault(name);
            if (string.Equals(name, PathName, StringComparison.OrdinalIgnoreCase))
            {
                var merged = MergePath(current, was, now);
                if (!string.Equals(merged, current ?? "", StringComparison.Ordinal)) changes[name] = merged;
                continue;
            }
            if (string.Equals(current, now, StringComparison.Ordinal)) continue;
            if (current is null || string.Equals(current, was, StringComparison.Ordinal)) changes[name] = now;
        }
        return changes;
    }

    /// <summary>
    /// The process's Path, less the folders the registry's Path has dropped since <paramref name="before"/>, followed by each
    /// folder of the registry's Path the process lacks, in the registry's order. Folders are compared without case, quotes or a
    /// trailing backslash; the process's own folders keep their place and their spelling, and with no folder gained or lost
    /// the process's Path comes back as it was, empty entries and all.
    /// </summary>
    public static string MergePath(string? process, string? before, string? after)
    {
        var dropped = Folders(before).Select(Key).Except(Folders(after).Select(Key), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = Folders(process).ToList();
        var kept = folders.Where(folder => !dropped.Contains(Key(folder))).ToList();
        var present = kept.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gained = false;
        foreach (var folder in Folders(after))
            if (present.Add(Key(folder))) { kept.Add(folder); gained = true; }
        return gained || kept.Count != folders.Count ? string.Join(';', kept) : process ?? "";
    }

    /// <summary>The folders of the Path <paramref name="after"/> that <paramref name="before"/> lacks, for the log.</summary>
    public static IEnumerable<string> AddedFolders(string? before, string? after)
    {
        var had = Folders(before).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Folders(after).Where(folder => !had.Contains(Key(folder)));
    }

    private static IEnumerable<string> Folders(string? path) => (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Key(string folder)
    {
        var bare = folder.Trim('"').TrimEnd('\\');
        return bare.Length == 0 ? folder : bare;
    }
}
