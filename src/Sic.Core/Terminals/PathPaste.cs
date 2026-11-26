namespace Sic.Core.Terminals;

/// <summary>
/// What SIC types into a terminal for files and folders, dropped or pasted: each path as a PowerShell single-quoted literal
/// when it needs one, separated by spaces, and never an Enter, so a drop only ever fills the prompt and the user decides what
/// runs. Single quotes because nothing inside them expands: a folder named <c>$env:TEMP</c> stays that folder.
/// </summary>
public static class PathPaste
{
    /// <summary>Quotes text as a PowerShell single-quoted literal when it contains anything the shell would parse.</summary>
    public static string Quote(string text)
    {
        if (text.Length == 0) return "''";
        var plain = text.All(character => char.IsLetterOrDigit(character) || character is '\\' or '/' or ':' or '.' or '-' or '_' or '~');
        return plain ? text : "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>The paths, each quoted as it needs, separated by single spaces. Empty entries are skipped.</summary>
    public static string Join(IEnumerable<string?> paths) => string.Join(' ', paths.OfType<string>().Where(path => path.Length > 0).Select(Quote));

    /// <summary>
    /// Dropped text: one existing, fully qualified path, bare or in the double quotes Explorer's Copy as path adds, is quoted
    /// like a dropped file; anything else goes in as it came. A relative path is left alone, because it could only be checked
    /// against SIC's own folder, not the terminal's.
    /// </summary>
    public static string FromText(string text, Func<string, bool>? exists = null)
    {
        var candidate = text.Trim();
        if (candidate.Length > 1 && candidate[0] == '"' && candidate[^1] == '"') candidate = candidate[1..^1];
        if (candidate.Length == 0 || candidate.IndexOfAny(['\r', '\n', '"']) >= 0 || !Path.IsPathFullyQualified(candidate)) return text;
        return (exists ?? (path => File.Exists(path) || Directory.Exists(path)))(candidate) ? Quote(candidate) : text;
    }
}
