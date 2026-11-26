namespace Sic.Core.State;

/// <summary>
/// A sound per pinned folder (<c>FolderSound</c> in settings.json), keyed by the folder as <see cref="PinnedFolders"/> keys it:
/// the id of a sound in the catalog, a built-in recipe's word or a file name from the sounds folder. It rings for a turn that
/// ends in a pane whose working folder is that folder or inside it, the nearest pinned folder with a sound winning, as the
/// folder's emoji does (<see cref="FolderMarks"/>; docs/decisions/completion-sounds.md). An id whose sound is gone, a file since
/// removed, falls back to the default when it rings; the entry stays, for the file's return, as a mark stays for a folder
/// pinned again.
/// </summary>
public static class FolderSounds
{
    /// <summary>The longest id kept: a file name.</summary>
    public const int MaxLength = 260;

    /// <summary>The id as it is kept: trimmed; null for nothing, or anything with a control character or too long.</summary>
    public static string? Normalize(string? id)
    {
        var trimmed = (id ?? "").Trim();
        if (trimmed.Length is 0 or > MaxLength || trimmed.Any(char.IsControl)) return null;
        return trimmed;
    }

    /// <summary>The saved map as it is kept: folders keyed as pins are, ids normalized, anything invalid dropped; compared without case.</summary>
    public static Dictionary<string, string> Clean(IDictionary<string, string>? sounds)
    {
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, id) in sounds ?? new Dictionary<string, string>())
        {
            var key = PinnedFolders.Normalize(folder);
            if (key.Length > 0 && Normalize(id) is { } valid) clean[key] = valid;
        }
        return clean;
    }

    /// <summary>The map with the folder's sound set, or taken away for a null or invalid id. A blank folder changes nothing.</summary>
    public static Dictionary<string, string> With(IDictionary<string, string>? sounds, string folder, string? id)
    {
        var clean = Clean(sounds);
        var key = PinnedFolders.Normalize(folder);
        if (key.Length == 0) return clean;
        if (Normalize(id) is { } valid) clean[key] = valid;
        else clean.Remove(key);
        return clean;
    }

    /// <summary>
    /// The sound id for a working folder: that of the nearest pinned folder which is the folder or holds it and has a sound, so a
    /// pinned folder without one takes its parent's. Null for no folder, none pinned above it, or none with a sound.
    /// </summary>
    public static string? For(IEnumerable<string> pinned, IReadOnlyDictionary<string, string> sounds, string? folder) => FolderMarks.Nearest(pinned, sounds, folder, Normalize);
}
