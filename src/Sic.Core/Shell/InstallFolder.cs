namespace Sic.Core.Shell;

/// <summary>
/// Whether this user can change the folder SicTerm is installed in (docs/decisions/installer.md, "For every user of a PC"). A copy
/// SicTerm's MSI installed for the whole PC lies in Program Files, where an ordinary user, or an administrator without elevation,
/// cannot write; Velopack's updater cannot update it from there, so such a copy leaves its updates to whoever installs the next MSI.
/// </summary>
public static class InstallFolder
{
    /// <summary>
    /// True when a file can be made in <paramref name="folder"/>: a scratch file opened and deleted at once. False for a folder
    /// that is missing or refuses the write.
    /// </summary>
    public static bool IsWritable(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        try
        {
            using var probe = new FileStream(Path.Combine(folder, ".sicterm-write-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { return false; }
    }
}
