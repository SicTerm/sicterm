using Sic.Core.State;

namespace Sic.App;

internal sealed partial class MainWindow
{
    /// <summary>
    /// How long settings.json and layout.json wait for quiet before they are written. A tab switch, a divider release, a pin, a
    /// setting and a window move each used to write their file at once, flushed to disk, on the UI thread, between the click and
    /// the repaint; now a burst of them writes once, off the thread, and a quit writes whatever is still waiting (<see cref="FlushState"/>).
    /// </summary>
    private static readonly TimeSpan SaveQuiet = TimeSpan.FromMilliseconds(300);
    private DeferredSave<AppPreferences> _savePreferences = null!;
    private DeferredSave<WorkspaceLayout> _saveLayout = null!;

    private void StartDeferredSaves()
    {
        void Failed(string file, Exception exception) =>
            DispatcherQueue.TryEnqueue(() => { if (!_closing) ShowError(file + " was not saved: " + exception.Message); });
        _savePreferences = new(DispatcherQueue, _preferencesStore, () => _preferences, SaveQuiet, exception => Failed("settings.json", exception));
        _saveLayout = new(DispatcherQueue, _layoutStore, CurrentLayout, SaveQuiet, exception => Failed("layout.json", exception));
    }

    /// <summary>Saves the preferences once they have been quiet for <see cref="SaveQuiet"/>.</summary>
    private void SavePreferences() => _savePreferences.Request();

    /// <summary>
    /// Saves the open tabs and panes once the layout has been quiet for <see cref="SaveQuiet"/>; the layout is taken when the write
    /// is made. A pinned tab's group follows the tab at the same time (<see cref="SyncGroups"/>).
    /// </summary>
    private void SaveLayout() { _saveLayout.Request(); SyncGroups(); RefreshWorkspacePanel(); }

    /// <summary>Writes whatever save is still waiting and returns once both files are on disk: before quitting, and before a check reads them.</summary>
    private void FlushState()
    {
        _savePreferences.Flush();
        _saveLayout.Flush();
    }
}
