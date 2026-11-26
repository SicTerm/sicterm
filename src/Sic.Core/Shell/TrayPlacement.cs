namespace Sic.Core.Shell;

/// <summary>
/// Whether SIC's tray icon starts on the taskbar or in the hidden icons. Windows 11 puts a new tray icon in the hidden
/// icons and keeps one entry per icon under <see cref="SettingsKey"/>, created when the icon is first shown: the program's
/// path, the icon's id, and <c>IsPromoted</c> once anyone has chosen (1 on the taskbar, 0 hidden). SIC writes 1 into its own
/// entry only while no choice is there, so the icon starts on the taskbar and a user who hides it keeps it hidden.
/// </summary>
/// <remarks>The key is not a documented API. Windows reads a change at once; if a later Windows drops it, nothing matches
/// and the icon is wherever Windows puts it.</remarks>
public static class TrayPlacement
{
    public const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <summary>One entry under <see cref="SettingsKey"/>: its subkey's name and the values SIC reads.</summary>
    /// <param name="ExecutablePath">As Windows writes it: a full path, or a known folder's id in braces and the rest of the
    /// path (<c>{6D809377-…}\WindowsApps\…</c> for Program Files).</param>
    /// <param name="HasGuid">The icon was added by GUID rather than by window and id.</param>
    public sealed record Entry(string Name, string? ExecutablePath, int? Uid, bool HasGuid, int? IsPromoted);

    /// <summary>
    /// The entry for the icon <paramref name="executable"/> adds with <paramref name="uid"/>, or null while Windows has made
    /// none. <paramref name="knownFolder"/> gives a known folder's path from its id, or null.
    /// </summary>
    public static Entry? Find(IEnumerable<Entry> entries, string executable, uint uid, Func<Guid, string?> knownFolder)
    {
        var target = Normalize(executable);
        return entries.FirstOrDefault(entry => !entry.HasGuid && entry.Uid == (int)uid
            && Expand(entry.ExecutablePath, knownFolder) is { } path && string.Equals(Normalize(path), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when nobody has placed the icon yet, so SIC may put it on the taskbar.</summary>
    public static bool ShouldPromote(Entry entry) => entry.IsPromoted is null;

    /// <summary>The path with a leading known-folder id replaced by that folder; null when the id is unknown or the path empty.</summary>
    public static string? Expand(string? path, Func<Guid, string?> knownFolder)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (path[0] != '{') return path;
        var close = path.IndexOf('}');
        if (close < 0 || !Guid.TryParse(path.AsSpan(0, close + 1), out var id)) return null;
        return knownFolder(id) is { Length: > 0 } folder ? Path.Join(folder, path[(close + 1)..].TrimStart('\\')) : null;
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\'); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
