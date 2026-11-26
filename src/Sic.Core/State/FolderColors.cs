using Sic.Core.Appearance;

namespace Sic.Core.State;

/// <summary>
/// A tab color per pinned folder (<c>FolderColor</c> in settings.json), keyed by the folder as <see cref="PinnedFolders"/> keys
/// it: a <see cref="TabColors"/> preset id or RRGGBB custom color. A tab whose first pane works in that folder or inside it, and
/// that has no color of its own, shows it; the nearest pinned folder with a color wins, as the folder's emoji does
/// (<see cref="FolderMarks"/>; docs/decisions/folder-colors.md). A color the user gives a tab is its own and stands over the
/// folder's, <see cref="TabColors.None"/> included.
/// </summary>
public static class FolderColors
{
    /// <summary>The color as it is kept: a preset id or upper-case RRGGBB; null for anything else, <see cref="TabColors.None"/> included.</summary>
    public static string? Normalize(string? color) => color == TabColors.None ? null : TabColors.Normalize(color);

    /// <summary>The saved map as it is kept: folders keyed as pins are, colors normalized, anything invalid dropped; compared without case.</summary>
    public static Dictionary<string, string> Clean(IDictionary<string, string>? colors)
    {
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, color) in colors ?? new Dictionary<string, string>())
        {
            var key = PinnedFolders.Normalize(folder);
            if (key.Length > 0 && Normalize(color) is { } valid) clean[key] = valid;
        }
        return clean;
    }

    /// <summary>The map with the folder's color set, or taken away for a null or invalid color. A blank folder changes nothing.</summary>
    public static Dictionary<string, string> With(IDictionary<string, string>? colors, string folder, string? color)
    {
        var clean = Clean(colors);
        var key = PinnedFolders.Normalize(folder);
        if (key.Length == 0) return clean;
        if (Normalize(color) is { } valid) clean[key] = valid;
        else clean.Remove(key);
        return clean;
    }

    /// <summary>
    /// The color for a working folder: that of the nearest pinned folder which is the folder or holds it and has a color, so a
    /// pinned folder without one takes its parent's. Null for no folder, none pinned above it, or none with a color.
    /// </summary>
    public static string? For(IEnumerable<string> pinned, IReadOnlyDictionary<string, string> colors, string? folder) => FolderMarks.Nearest(pinned, colors, folder, Normalize);
}
