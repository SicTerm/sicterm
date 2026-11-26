namespace Sic.Core.Shell;

/// <summary>
/// What the window's close and minimize buttons do (Settings → General → Close and minimize; docs/decisions/close-and-minimize.md).
/// "tray", the default: both hide SicTerm to the tray, its sessions running, with no taskbar button. "close-to-tray": close does
/// that and minimize leaves the window on the taskbar, SicTerm's way until October 15, 2026. "quit": close quits SicTerm as Quit
/// does, asking first about running agents, and minimize leaves the window on the taskbar. Kept as a word so a settings file
/// reads plainly, and normalized on load so an unknown word is the default.
/// </summary>
public static class CloseAndMinimize
{
    public const string Tray = "tray", CloseToTray = "close-to-tray", Quit = "quit";
    public static IReadOnlyList<string> Choices { get; } = [Tray, CloseToTray, Quit];

    public static string Normalize(string? choice) => choice?.Trim().ToLowerInvariant() switch
    {
        CloseToTray => CloseToTray,
        Quit => Quit,
        _ => Tray
    };

    /// <summary>Whether closing the window hides it to the tray rather than quitting.</summary>
    public static bool ClosesToTray(string? choice) => Normalize(choice) != Quit;

    /// <summary>Whether minimizing the window hides it to the tray rather than leaving it on the taskbar.</summary>
    public static bool MinimizesToTray(string? choice) => Normalize(choice) == Tray;

    public static string Label(string? choice) => Normalize(choice) switch
    {
        CloseToTray => "Close to tray, minimize to taskbar",
        Quit => "Close quits, minimize to taskbar",
        _ => "Close and minimize to tray"
    };
}
