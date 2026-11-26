using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sic.Core.Sessions;

/// <summary>
/// Whether two paths name the same folder: written alike first (full paths in any case, with or without a trailing
/// separator), and failing that as Windows resolves them, with junctions, symbolic links and substituted drives followed to
/// the folder they lead to. Hermes may record a session's folder that way (its gateway client takes <c>--in</c> through
/// Python's <c>Path.resolve</c>), so a pane started in <c>C:\work\app</c>, where <c>C:\work</c> is a junction to <c>D:\work</c>,
/// can find its session recorded in <c>D:\work\app</c> (docs/decisions/hermes-session-names.md).
/// </summary>
public static class FolderIdentity
{
    public static bool Same(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        if (string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase)) return true;
        return Resolved(first) is { } one && Resolved(second) is { } other && string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The full path without a trailing separator, or the path as given when it is not one.</summary>
    public static string Normalize(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>
    /// The folder's final path, links followed (GetFinalPathNameByHandle, which Python's resolve uses on Windows), without the
    /// <c>\\?\</c> prefix; null for a folder that cannot be opened, or off Windows.
    /// </summary>
    public static string? Resolved(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var handle = CreateFileW(Normalize(path), ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) return null;
            var buffer = new char[512];
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length >= buffer.Length)
            {
                buffer = new char[length + 1];
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            }
            if (length == 0 || length >= buffer.Length) return null;
            var final = new string(buffer, 0, (int)length);
            if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
            else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
            return Path.TrimEndingDirectorySeparator(final);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    private const uint ReadAttributes = 0x80, ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);
}
