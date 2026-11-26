using System.Globalization;

namespace Sic.Core.State;

/// <summary>
/// An emoji per pinned folder (<c>FolderEmoji</c> in settings.json), keyed by the folder as <see cref="PinnedFolders"/> keys it
/// and shown on the folder's rows and on every tab and pane whose working folder is that folder or inside it, the nearest
/// marked pinned folder winning (docs/decisions/folder-emoji.md). A mark is one text element: an emoji with its skin tone,
/// joiners and variation selector counts as one, as does any single character; anything longer, blank, or with a control or
/// white-space character is refused rather than cut. A mark kept for a folder that is no longer pinned stays in the file and
/// shows again when the folder is pinned again.
/// </summary>
public static class FolderMarks
{
    /// <summary>The longest mark kept, in UTF-16 units: the longest emoji sequences in use are under 20.</summary>
    public const int MaxLength = 32;

    /// <summary>The mark as it is kept: trimmed and one text element; null for anything else, nothing included.</summary>
    public static string? Normalize(string? mark)
    {
        var trimmed = (mark ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength || trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;
        return new StringInfo(trimmed).LengthInTextElements == 1 ? trimmed : null;
    }

    /// <summary>The last text element of what was typed into a box, so a second emoji replaces the first; null for nothing valid.</summary>
    public static string? Last(string? typed)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0) return null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        string? last = null;
        while (elements.MoveNext()) last = elements.GetTextElement();
        return Normalize(last);
    }

    /// <summary>The saved map as it is kept: folders keyed as pins are, marks normalized, anything invalid dropped; compared without case.</summary>
    public static Dictionary<string, string> Clean(IDictionary<string, string>? marks)
    {
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, mark) in marks ?? new Dictionary<string, string>())
        {
            var key = PinnedFolders.Normalize(folder);
            if (key.Length > 0 && Normalize(mark) is { } valid) clean[key] = valid;
        }
        return clean;
    }

    /// <summary>The map with the folder marked, or unmarked for a null or invalid mark. A blank folder changes nothing.</summary>
    public static Dictionary<string, string> With(IDictionary<string, string>? marks, string folder, string? mark)
    {
        var clean = Clean(marks);
        var key = PinnedFolders.Normalize(folder);
        if (key.Length == 0) return clean;
        if (Normalize(mark) is { } valid) clean[key] = valid;
        else clean.Remove(key);
        return clean;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it, compared as pins are compared.</summary>
    public static bool IsUnder(string? folder, string? path) => Under(PinnedFolders.Normalize(folder), PinnedFolders.Normalize(path));

    /// <summary><see cref="IsUnder"/> for two folders already normalized as pins are.</summary>
    private static bool Under(string root, string inner)
    {
        if (root.Length == 0 || inner.Length == 0) return false;
        if (string.Equals(root, inner, StringComparison.OrdinalIgnoreCase)) return true;
        return inner.StartsWith(root.EndsWith('\\') ? root : root + '\\', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The mark for a working folder: that of the nearest pinned folder which is the folder or holds it and has a mark, so a
    /// pinned folder without one shows its parent's. Null for no folder, none pinned above it, or none marked.
    /// </summary>
    public static string? For(IEnumerable<string> pinned, IReadOnlyDictionary<string, string> marks, string? folder) => Nearest(pinned, marks, folder, Normalize);

    /// <summary>
    /// The value kept for the nearest pinned folder that is <paramref name="folder"/> or holds it and has a valid one, by
    /// <paramref name="normalize"/>: how a folder's mark and its sound are both found. Null for no folder, none pinned above
    /// it, or none with a value. <paramref name="values"/> is keyed as the pins are (<see cref="Clean"/>), compared without case;
    /// it runs for every tab and pane header each time their status is drawn, so the folder is normalized once and the map read once per pin.
    /// </summary>
    public static string? Nearest(IEnumerable<string> pinned, IReadOnlyDictionary<string, string> values, string? folder, Func<string?, string?> normalize)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var inner = PinnedFolders.Normalize(folder);
        string? best = null;
        var bestLength = -1;
        foreach (var candidate in pinned)
        {
            var key = PinnedFolders.Normalize(candidate);
            if (key.Length <= bestLength || !Under(key, inner) || !values.TryGetValue(key, out var kept) || normalize(kept) is not { } valid) continue;
            best = valid; bestLength = key.Length;
        }
        return best;
    }
}
