using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Settings → Updates and the checks behind it (<see cref="Updates"/>): a quiet check a little after launch and every six hours
/// while SIC runs, a held notice once a newer release has downloaded, Restart now, and the hand-off to Velopack's updater as
/// SIC quits, after its agents have been dealt with.
/// </summary>
internal sealed partial class MainWindow
{
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(20), UpdateCheckInterval = TimeSpan.FromHours(6);
    private Updates? _updates;
    private TextBlock _updateStatus = null!;
    private Button _checkUpdates = null!, _restartToUpdate = null!;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _updateTimer;
    private bool _checkingUpdates, _restartAfterUpdate;
    private string? _updateProblem;
    private DateTimeOffset? _updatesCheckedAt;

    private void CreateUpdates()
    {
        try { _updates = new Updates(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _updateProblem = exception.Message; }
    }

    /// <summary>Starts the quiet checks: one a little after launch, then every six hours. Only an installed copy checks.</summary>
    private void StartUpdateChecks()
    {
        if (_options.SmokeTest || _updates is not { UpdatesItself: true }) return;
        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = UpdateCheckInterval;
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(quiet: true);
        _updateTimer.Start();
        _ = FirstCheckAsync();
        async Task FirstCheckAsync()
        {
            try { await Task.Delay(FirstUpdateCheck, _lifetime.Token); }
            catch (OperationCanceledException) { return; }
            await CheckForUpdatesAsync(quiet: true);
        }
    }

    /// <summary>
    /// Checks for a newer release and downloads it. A new release is announced once; a quiet check says nothing when it finds
    /// nothing or fails, since the next one tries again. Settings shows the outcome either way.
    /// </summary>
    internal async Task CheckForUpdatesAsync(bool quiet)
    {
        if (_updates is not { UpdatesItself: true } updates || _checkingUpdates || _closing) return;
        _checkingUpdates = true; _updateProblem = null;
        RefreshUpdateState("Checking for updates…");
        try
        {
            var before = updates.Ready?.Version;
            var version = await updates.CheckAsync(_lifetime.Token);
            _updatesCheckedAt = DateTimeOffset.Now;
            if (version is not null && updates.Ready?.Version != before) AnnounceUpdate(version);
            else if (version is null && !quiet) NotifyBriefly("SicTerm is up to date", "Version " + updates.Version + " is the newest release.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _updateProblem = Updates.Describe(exception);
            if (!quiet) Notify(InfoBarSeverity.Warning, "Updates", _updateProblem);
        }
        finally { _checkingUpdates = false; RefreshUpdateState(); }
    }

    private void AnnounceUpdate(string version)
    {
        var restart = Theme.TextButton("Restart now", "Quit SicTerm and start version " + version);
        restart.Click += async (_, _) => await RestartToUpdateAsync();
        Notify(InfoBarSeverity.Informational, "Update ready", "SicTerm " + version + " is downloaded and installs when SicTerm quits.", restart);
    }

    /// <summary>Quits as Quit does, asking about running agents as usual, and starts the new version once it is applied.</summary>
    internal async Task RestartToUpdateAsync()
    {
        if (_updates?.Ready is null) return;
        _restartAfterUpdate = true;
        await CloseAsync();
        if (!_closing) _restartAfterUpdate = false;
    }

    /// <summary>As SIC quits: a downloaded release goes to the updater, which applies it once this process has exited.</summary>
    private void ApplyUpdateOnQuit()
    {
        try { _updates?.ApplyOnExit(_restartAfterUpdate); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* The next launch applies it instead. */ }
    }

    private void AddUpdatesSettings(StackPanel stack)
    {
        stack.Children.Add(Section("Updates"));
        _updateStatus = _theme.Tertiary(""); _updateStatus.TextWrapping = TextWrapping.Wrap; _updateStatus.TextTrimming = TextTrimming.None;
        stack.Children.Add(_updateStatus);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _checkUpdates = Theme.IconTextButton("", "Check for updates");
        _checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync(quiet: false);
        Explain(_checkUpdates, "SicTerm's releases are on GitHub. A newer release downloads in the background and installs when SicTerm quits.");
        _restartToUpdate = Theme.TextButton("Restart to update", "Quit SicTerm and start the downloaded version");
        _restartToUpdate.Click += async (_, _) => await RestartToUpdateAsync();
        buttons.Children.Add(_checkUpdates); buttons.Children.Add(_restartToUpdate);
        stack.Children.Add(buttons);
        RefreshUpdateState();
    }

    private void RefreshUpdateState(string? busy = null)
    {
        if (_updateStatus is null) return;
        _checkUpdates.IsEnabled = _updates is { UpdatesItself: true } && !_checkingUpdates;
        _restartToUpdate.Visibility = _updates?.Ready is null ? Visibility.Collapsed : Visibility.Visible;
        _updateStatus.Text = busy ?? UpdateStatusText();
    }

    private string UpdateStatusText()
    {
        if (_updates is null) return "Updates are unavailable: " + _updateProblem;
        if (!_updates.IsInstalled)
            return "This copy is not installed, so it does not update itself: it runs from " + AppContext.BaseDirectory.TrimEnd('\\') + ". Copies installed with SicTerm's Setup update themselves.";
        var text = "Version " + _updates.Version + ".";
        if (_updates.IsManaged)
            return text + " Installed for every user of this PC, in " + Updates.InstallRoot.TrimEnd('\\') + ", so it does not update itself: a newer SicTerm MSI installed on the PC updates it.";
        if (_updates.Ready is { } ready) return text + " Version " + ready.Version + " is downloaded and installs when SicTerm quits.";
        if (_updateProblem is not null) return text + " " + _updateProblem;
        if (_updatesCheckedAt is { } at) return text + " Up to date as of " + at.ToString("t") + ".";
        return text + " Updates come from " + (Updates.Origin == Updates.Repository ? "GitHub" : Updates.Origin) + ".";
    }
}
