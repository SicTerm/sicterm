namespace Sic.Core.State;

/// <summary>
/// The name of a folder made from the Folders panel (a folder's menu → New folder; November 12, 2026): what Windows takes for
/// one, said as a fact for the dialog to show, and the name the dialog starts with.
/// </summary>
public static class FolderNames
{
    /// <summary>The name the dialog offers, as File Explorer does.</summary>
    public const string Default = "New folder";

    /// <summary>The longest name NTFS keeps.</summary>
    public const int MaxLength = 255;

    private static readonly char[] Refused = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>The name as it is used: without the spaces around it, which Windows would drop from its end anyway.</summary>
    public static string Clean(string? name) => (name ?? "").Trim();

    /// <summary>Why a name cannot be a folder's, or null for one that can. The name is taken as <see cref="Clean"/> leaves it.</summary>
    public static string? Problem(string? name)
    {
        var clean = Clean(name);
        if (clean.Length == 0) return "A folder needs a name.";
        if (clean.Length > MaxLength) return "A folder name can be 255 characters at most.";
        if (clean.IndexOfAny(Refused) >= 0 || clean.Any(char.IsControl)) return "A folder name cannot contain < > : \" / \\ | ? *";
        if (clean.EndsWith('.')) return "A folder name cannot end with a period.";
        // Windows keeps its device names with any extension too: NUL.txt is still NUL.
        var stem = clean.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem)) return "Windows keeps the name " + stem.ToUpperInvariant() + " for itself.";
        return null;
    }

    /// <summary>The name, or the first of "name (2)", "name (3)"… that is not taken.</summary>
    public static string Unused(string name, Func<string, bool> taken)
    {
        if (!taken(name)) return name;
        for (var number = 2; ; number++)
        {
            var next = name + " (" + number + ")";
            if (!taken(next)) return next;
        }
    }
}
