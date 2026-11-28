using System.Text;

namespace Sic.Core.Shell;

/// <summary>
/// A PowerShell tab's own status report, over the Program Status Protocol (OSC 7501) the agents use: working from the moment
/// Enter sends a command line, idle when the prompt comes back. Without it a PowerShell tab was judged by its output alone, so
/// an installer that paused more than three seconds between lines dropped from the working ring to the running dot, and out of
/// view read as a finished command, the blinking Your turn dot (winget installing PowerShell 7 in a Windows Sandbox run,
/// November 27, 2026). With a report standing, <see cref="Agents.PresenceTracker"/> takes it over the output.
/// <para>
/// The hook runs after the profile, so the profile's own prompt (oh-my-posh, Starship) is kept and wrapped. Working is said by
/// wrapping PSConsoleHostReadLine, which PSReadLine defines and the console host calls for each line; PSReadLine is loaded by
/// the first prompt, not before -Command runs, so the wrap is made there. Where PSReadLine is not loaded, nothing is reported at
/// all, and the output decides as before: an idle with no working would hold the tab idle through every command.
/// </para>
/// </summary>
public static class ShellStatus
{
    public const string Working = "\u001b]7501;state=working\u0007";
    public const string Idle = "\u001b]7501;state=idle\u0007";

    /// <summary>
    /// The hook, for Windows PowerShell 5.1 and PowerShell 7 alike. The original prompt runs first, so it still sees the last
    /// command's <c>$?</c>, which prompts that show a failure read.
    /// </summary>
    public const string Script =
        "$global:SicStatus = $false; $global:SicPrompt = $function:prompt; " +
        "function global:prompt { " +
        "$sicText = & $global:SicPrompt; " +
        "if (-not $global:SicStatus -and (Get-Command -Name PSConsoleHostReadLine -CommandType Function -ErrorAction SilentlyContinue)) { " +
        "$global:SicReadLine = $function:PSConsoleHostReadLine; " +
        "function global:PSConsoleHostReadLine { $sicLine = & $global:SicReadLine; [Console]::Write([char]27 + ']7501;state=working' + [char]7); $sicLine }; " +
        "$global:SicStatus = $true }; " +
        "if ($global:SicStatus) { [Console]::Write([char]27 + ']7501;state=idle' + [char]7) }; " +
        "$sicText }";

    /// <summary>
    /// The arguments a PowerShell tab starts with: -NoLogo, then <paramref name="extra"/> (the PowerShell card's own), then the
    /// hook, run with the profile loaded and the shell left open.
    /// </summary>
    public static IReadOnlyList<string> Arguments(IEnumerable<string>? extra = null) =>
        ["-NoLogo", .. extra ?? [], "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script))];
}
