namespace Sic.App;

internal static class Paths
{
    /// <summary>Opens a folder in File Explorer. Throws when Explorer cannot be started; Explorer itself opens its default folder for one that does not exist.</summary>
    internal static void OpenInExplorer(string folder) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });

    /// <summary>The last folder name, or the path itself for a root.</summary>
    internal static string Leaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return leaf.Length > 0 ? leaf : path;
    }

    /// <summary>Keeps the drive, the parent and the leaf of a long path: C:\…\personal\super-intelligent-chat.</summary>
    internal static string Shorten(string? path, int keepTail = 2)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var parts = trimmed.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= keepTail + 2) return trimmed;
        return parts[0] + "\\…\\" + string.Join('\\', parts[^keepTail..]);
    }

    /// <summary>
    /// The whole path when it fits; otherwise the longest drive\…\tail form that does, so the folder's own name always shows.
    /// Falls back to drive\…\leaf, which the caller may still end-trim.
    /// </summary>
    internal static string Fit(string? path, Func<string, bool> fits)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var full = Path.TrimEndingDirectorySeparator(path);
        if (fits(full)) return full;
        var parts = full.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Length;
        for (var keep = parts - 3; keep > 1; keep--)
        {
            var candidate = Shorten(full, keep);
            if (fits(candidate)) return candidate;
        }
        return Shorten(full, 1);
    }

    /// <summary>"Just now", "5 min ago", "Yesterday", "Sep 24", "Sep 24, 2025".</summary>
    internal static string RelativeTime(DateTimeOffset instant, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        var local = instant.ToLocalTime();
        var age = reference - instant;
        if (age < TimeSpan.FromMinutes(1)) return "Just now";
        if (age < TimeSpan.FromHours(1)) return (int)age.TotalMinutes + " min ago";
        if (age < TimeSpan.FromHours(24) && local.Date == reference.ToLocalTime().Date) return (int)age.TotalHours + " h ago";
        if (local.Date == reference.ToLocalTime().Date.AddDays(-1)) return "Yesterday";
        if (local.Year == reference.Year) return local.ToString("MMM d");
        return local.ToString("MMM d, yyyy");
    }
}
