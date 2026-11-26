namespace Sic.Core.State;

/// <summary>
/// Which tabs go together when the tabs are consolidated: those whose first pane works in the same folder, the folder that
/// gives the tab its emoji. A tab is moved whole into the first tab of its group in strip order, so a tab split by hand keeps
/// its layout. The folder is compared as a full path without its trailing separator, ignoring case, as Windows does; a tab
/// whose first pane has no folder, or one that is not a full path, joins no group.
/// </summary>
public static class TabConsolidation
{
    /// <summary>The key a folder groups by, or null for no folder or one that is not a full path.</summary>
    public static string? Key(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToUpperInvariant(); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>
    /// The groups of two or more tabs that share a folder, each in the order given (the strip's), and the groups in the order
    /// of their first tabs. A tab alone in its folder is in no group.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<T>> Groups<T>(IEnumerable<T> tabs, Func<T, string?> folderOf)
    {
        var byKey = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var tab in tabs)
        {
            if (Key(folderOf(tab)) is not { } key) continue;
            if (!byKey.TryGetValue(key, out var group)) { group = []; byKey[key] = group; order.Add(key); }
            group.Add(tab);
        }
        return order.Select(key => byKey[key]).Where(group => group.Count > 1).Select(group => (IReadOnlyList<T>)group).ToList();
    }
}
