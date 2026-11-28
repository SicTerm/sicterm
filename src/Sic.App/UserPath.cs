using System.Runtime.InteropServices;
using Microsoft.Win32;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// The account's own Path, as Windows keeps it in the registry (HKCU\Environment), for the one change SicTerm makes outside its
/// own folder, and only when asked: Add to PATH on Claude Code's row, whose installer leaves its folder off Path and says to add
/// it by hand (2.1.332; a Windows Sandbox run, November 27, 2026). The value is read and written as it is stored: one folder
/// goes on the end, every other entry keeps its spelling and its variables, and the value keeps its kind. The usual one-line
/// command reads the value expanded and writes all of it back, which turns %USERPROFILE% entries into fixed text. No admin
/// rights are needed for the account's own Path.
/// </summary>
internal static class UserPath
{
    internal const string EnvironmentKey = "Environment";

    /// <summary>Whether <paramref name="folder"/> is on the account's Path or the PC's, as a new logon would have it.</summary>
    internal static bool Contains(string folder) =>
        PathList.Contains(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User), folder)
        || PathList.Contains(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine), folder);

    /// <summary>
    /// Adds <paramref name="folder"/> to the end of the Path under <paramref name="key"/> (the account's, unless a test names
    /// another) and tells Windows the environment changed, so programs started from now on have it. False when it was already there.
    /// </summary>
    internal static bool Add(string folder, string key = EnvironmentKey)
    {
        using var environment = Registry.CurrentUser.CreateSubKey(key, writable: true);
        var stored = environment.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (PathList.Append(stored, folder) is not { } added) return false;
        // A Path that was plain text stays plain text; one that is not there yet is made the kind Windows itself makes.
        var kind = stored is not null && environment.GetValueKind("Path") == RegistryValueKind.String ? RegistryValueKind.String : RegistryValueKind.ExpandString;
        environment.SetValue("Path", added, kind);
        if (key == EnvironmentKey) Announce();
        return true;
    }

    /// <summary>WM_SETTINGCHANGE "Environment" to every top-level window, as an installer sends it; a window that does not answer is passed over.</summary>
    private static void Announce()
    {
        const int broadcast = 0xFFFF, settingChange = 0x001A, abortIfHung = 0x0002;
        SendMessageTimeout(new IntPtr(broadcast), settingChange, IntPtr.Zero, "Environment", abortIfHung, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);
}
