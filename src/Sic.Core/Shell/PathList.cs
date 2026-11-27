namespace Sic.Core.Shell;

/// <summary>
/// A Path value as a list of folders: finding a program on it, asking whether a folder is on it, and adding one. Folders are
/// compared with their variables expanded, without case, quotes or a trailing backslash.
/// </summary>
public static class PathList
{
    /// <summary>The first <paramref name="file"/> in the folders of <paramref name="path"/> (the process's Path when null), as a full path; null when none holds it.</summary>
    public static string? Find(string file, string? path = null, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        foreach (var folder in Folders(path ?? Environment.GetEnvironmentVariable("PATH")))
        {
            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(folder.Trim('"'), file)); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Whether <paramref name="folder"/> is one of the folders of <paramref name="path"/>.</summary>
    public static bool Contains(string? path, string folder)
    {
        var key = Key(folder);
        return Folders(path).Any(entry => Key(entry).Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <paramref name="path"/> with <paramref name="folder"/> added at its end, where nothing it holds is put behind the new
    /// folder; null when the folder is already there. Every other entry comes back as it was written, variables unexpanded and
    /// empty entries kept, so the value can go back to the registry as it came.
    /// </summary>
    public static string? Append(string? path, string folder)
    {
        if (Contains(path, folder)) return null;
        var current = path ?? "";
        return current.Length == 0 ? folder : current.TrimEnd(';') + ";" + folder;
    }

    private static IEnumerable<string> Folders(string? path) => (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Key(string folder) => Path.TrimEndingDirectorySeparator(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')));
}

/// <summary>What a PowerShell pane shows when it is waiting for a command, so text typed for the person lands at its prompt.</summary>
public static class ShellPrompt
{
    /// <summary>
    /// Whether the last line of <paramref name="screen"/> that holds anything is PowerShell's own prompt with nothing typed
    /// after it: "PS C:\Users\name>". A profile's own prompt does not match; the caller then waits for the screen to settle.
    /// </summary>
    public static bool Ready(string? screen)
    {
        var last = (screen ?? "").Split('\n').Select(line => line.TrimEnd()).LastOrDefault(line => line.Length > 0);
        return last is not null && last.StartsWith("PS ", StringComparison.Ordinal) && last.EndsWith('>');
    }
}
