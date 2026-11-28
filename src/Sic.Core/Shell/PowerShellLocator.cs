namespace Sic.Core.Shell;

/// <summary>
/// Where the PowerShell SicTerm runs in is: PowerShell 7 wherever it is installed, otherwise the Windows PowerShell 5.1 that
/// comes with Windows. The app starts every pane and detection in it (AgentDetection.PowerShellPath), and the tests that start
/// a real shell find theirs the same way.
/// </summary>
public static class PowerShellLocator
{
    /// <summary>Windows PowerShell 5.1, by its full path in System32, so a powershell.exe elsewhere on PATH is never the one taken.</summary>
    public static string WindowsPowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>
    /// PowerShell 7: the MSI's folder under Program Files first, then the first pwsh.exe on PATH, which is how the MSIX
    /// (the Store's, and winget's since 7.6; the only installer from 7.7, which has no MSI) and a ZIP install are reached; the
    /// MSIX through its alias under %LOCALAPPDATA%\Microsoft\WindowsApps. Null when there is none.
    /// </summary>
    public static string? FindPowerShell7(string? path = null, string? programFiles = null, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var standard = Path.Combine(programFiles ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (exists(standard)) return standard;
        foreach (var folder in (path ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = folder.Trim().Trim('"');
            if (trimmed.Length == 0) continue;
            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(trimmed, "pwsh.exe")); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The shell SicTerm runs in: PowerShell 7 when <see cref="FindPowerShell7"/> finds it, otherwise Windows PowerShell; null when neither is there.</summary>
    public static string? Find() => FindPowerShell7() ?? (File.Exists(WindowsPowerShell) ? WindowsPowerShell : null);
}
