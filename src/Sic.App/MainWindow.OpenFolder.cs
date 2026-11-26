using Sic.Core.Shell;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Open in SIC, from File Explorer's right-click menu (docs/decisions/explorer-menu.md): <c>SicTerm.exe --open &lt;folder&gt;</c>
/// brings the window forward and opens the New session dialog with that folder filled in; with <c>--agent &lt;id&gt;</c> it starts
/// that agent there at once. The request arrives on the command line when this launch is the first SIC for its data folder,
/// and in a window message from the second launch otherwise (<see cref="SingleInstance"/>, <see cref="TrayIcon"/>).
/// </summary>
internal sealed partial class MainWindow
{
    private const string OpenTitle = "Open in SicTerm";

    /// <summary>
    /// Carries a request out. A folder that is not there, or a dialog already up, gets a held notice naming the folder and nothing
    /// else changes; an agent that is off in Settings gets the dialog instead, with a notice saying so. Reports its own failures,
    /// since it is called from the message pump and the window's load.
    /// </summary>
    private async Task OpenRequestedAsync(OpenRequest? request)
    {
        try
        {
            Diagnostics.Log("open: " + (request is null ? "(unreadable)" : request.Folder + (request.AgentId is null ? "" : " with " + request.AgentId)));
            ShowFromTray();
            if (request is null) { ShowError("The Open in SicTerm request could not be read."); return; }
            if (OpenRequest.Problem(request.Folder, Directory.Exists) is { } problem) { Notify(InfoBarSeverity.Warning, OpenTitle, problem); return; }
            if (_dialogsOpen > 0) { Notify(InfoBarSeverity.Warning, OpenTitle, request.Folder + " was not opened: a dialog is open."); return; }
            if (request.AgentId is { } agent)
            {
                if (AgentOn(agent) && (agent == "shell" || _preferences.Agents.Any(definition => definition.Id == agent)))
                {
                    if (!await OfferFolderAsync(request.Folder)) return;
                    await StartNewAsync(agent, request.Folder);
                    Diagnostics.Log("open: " + agent + " started in " + request.Folder);
                    return;
                }
                Notify(InfoBarSeverity.Warning, OpenTitle, Theme.AgentName(agent) + " is off in Settings → Agents, so the New session dialog opened instead.");
            }
            await ShowNewSessionAsync(request.Folder);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("open failed: " + exception); ShowError(exception.Message); }
    }

    /// <summary>Whether File Explorer's entry points at this copy of SIC.</summary>
    private static bool OwnsExplorerMenu(string? registered)
        => registered is not null && string.Equals(registered, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps the submenu in step with Settings → Agents while the entry is this copy's: at launch, and when an agent is switched.
    /// A copy with a data folder of its own (the smoke, a review) leaves the menu alone; a registry failure here is not worth a
    /// notice at launch, since the switch in Settings reads and reports the registry when it is opened.
    /// </summary>
    private void SyncExplorerMenu()
    {
        if (_options.Isolated || Environment.ProcessPath is not { } executable) return;
        try
        {
            if (!OwnsExplorerMenu(ExplorerMenuRegistration.RegisteredExecutable())) return;
            var entries = ExplorerMenuRegistration.ExpectedEntries(executable, _preferences.Agents, IconSourceFor, LibraryIconFor);
            if (!ExplorerMenuRegistration.IsCurrent(entries)) ExplorerMenuRegistration.Refresh(entries);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}
