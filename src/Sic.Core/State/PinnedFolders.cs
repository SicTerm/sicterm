namespace Sic.Core.State;

/// <summary>
/// The folders pinned at the top of the Workspaces tree, kept in the order they were pinned. A folder is one pin however it is
/// written: separators either way, any case, with or without a trailing separator, so a pin made from the tree and one made
/// from a typed path meet. A drive root keeps its separator, since "C:" alone names the drive's current directory.
/// </summary>
public static class PinnedFolders
{
    public static string Normalize(string? path)
    {
        var trimmed = (path ?? "").Trim().Replace('/', '\\').TrimEnd('\\');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + '\\' : trimmed;
    }

    public static bool Same(string? a, string? b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool Contains(IEnumerable<string> pinned, string path) => pinned.Any(item => Same(item, path));

    /// <summary>The saved list as it is kept: normalized, without blanks, each folder once, first pin first.</summary>
    public static List<string> Clean(IEnumerable<string>? pinned) =>
        (pinned ?? []).Select(Normalize).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The list with the folder pinned at the end when it was not, or removed when it was. A blank path changes nothing.</summary>
    public static List<string> Toggle(IEnumerable<string> pinned, string path)
    {
        var list = Clean(pinned);
        var key = Normalize(path);
        if (key.Length == 0) return list;
        if (list.RemoveAll(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(key);
        return list;
    }
}
