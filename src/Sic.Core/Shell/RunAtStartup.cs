namespace Sic.Core.Shell;

/// <summary>
/// Settings → General → Run at Startup (docs/decisions/run-at-startup.md, November 12, 2026): SicTerm started as its person
/// signs in to Windows, by a value under the person's own Run key. The registry is the state, as it is for Open in SicTerm:
/// nothing is kept in settings.json, so the switch tells the truth after an update, a move, or a change made in Task Manager.
/// </summary>
public static class RunAtStartup
{
    /// <summary>The person's Run key, under HKEY_CURRENT_USER: no administrator is needed, and no other person of the PC is touched.</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where Task Manager's Startup apps page and Settings → Apps → Startup keep an entry turned off, under HKEY_CURRENT_USER.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>The value's name, which Task Manager shows beside the exe's own description.</summary>
    public const string ValueName = "SicTerm";

    /// <summary>The command Windows runs at sign-in: the exe, quoted for a path with spaces.</summary>
    public static string Command(string executable) => "\"" + executable + "\"";

    /// <summary>The exe a Run command starts, or null for a value that is not one: quoted, or bare up to its first space.</summary>
    public static string? ExecutableOf(string? command)
    {
        var text = (command ?? "").Trim();
        if (text.Length == 0) return null;
        if (text[0] != '"') return text.Split(' ')[0];
        var end = text.IndexOf('"', 1);
        return end > 1 ? text[1..end] : null;
    }

    /// <summary>
    /// Whether Windows runs an entry, by its value under <see cref="ApprovedKey"/>: no value there is yes, and one whose first
    /// byte is odd (3, as Task Manager writes it for Disabled) is no.
    /// </summary>
    public static bool Approved(byte[]? state) => state is not { Length: > 0 } || (state[0] & 1) == 0;

    /// <summary>True when the entry starts this exe and Windows runs it.</summary>
    public static bool IsOn(string? command, byte[]? state, string executable) =>
        Approved(state) && ExecutableOf(command) is { } registered && string.Equals(registered, executable, StringComparison.OrdinalIgnoreCase);
}
