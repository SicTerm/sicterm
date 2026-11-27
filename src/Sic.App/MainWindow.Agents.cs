using System.Text.Json;
using Sic.Core.Agents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Settings → Agents: a card per agent with its tile, name, whether detection found it, and its on/off switch in view;
/// where it was found and its command and history home a click away. The switch applies at once
/// (docs/plans/2026-10-05-agent-switches-and-settings-tabs.md). Under the cards, Plans: whether the usage limits show.
/// </summary>
internal sealed partial class MainWindow
{
    private sealed record AgentRow(ToggleSwitch Switch, FrameworkElement Identity, TextBlock Status, TextBlock Where);
    private readonly Dictionary<string, AgentRow> _agentRows = new(StringComparer.Ordinal);
    /// <summary>The last detection's answer per agent; empty while none has finished.</summary>
    private readonly Dictionary<string, DetectedAgent> _detection = new(StringComparer.Ordinal);
    private bool _detecting, _detectionFailed, _syncingAgentSwitches;
    private TextBlock _agentSummary = null!;

    private void BuildAgentsPage(StackPanel page)
    {
        page.Children.Add(Section("Agents"));
        // No PowerShell 7 or no agent, until closed here (MainWindow.Setup.cs).
        page.Children.Add(BuildSetupBar());
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _agentSummary = _theme.Caption(""); _agentSummary.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_agentSummary);
        var detect = Theme.IconTextButton("", "Detect again", "Search PATH and your PowerShell profile");
        detect.Click += async (_, _) => await DetectToolsAsync();
        Grid.SetColumn(detect, 1); top.Children.Add(detect);
        page.Children.Add(top);
        foreach (var definition in _preferences.Agents) page.Children.Add(BuildAgentCard(definition));
        RefreshAgentRows();
        // The plans behind the agents (MainWindow.Limits.cs); under General until October 15, 2026.
        page.Children.Add(Section("Plans"));
        var usageLimits = new ToggleSwitch { Header = "Show usage limits", IsOn = _preferences.ShowUsageLimits };
        usageLimits.Toggled += (_, _) => SetShowUsageLimits(usageLimits.IsOn);
        Explain(usageLimits, "Claude and ChatGPT plan limits in the status bar, and each pane's model and context window in its header. Replaces Claude Code's status line.");
        page.Children.Add(usageLimits);
    }

    private Border BuildAgentCard(AgentDefinition definition)
    {
        var id = definition.Id;
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new Grid { ColumnSpacing = 10 };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = _theme.AgentTile(id, 24); tile.VerticalAlignment = VerticalAlignment.Center;
        identity.Children.Add(tile);
        var labels = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(_theme.BodyText(definition.Name, 13, 600));
        var status = _theme.BodyText("", 12);
        labels.Children.Add(status);
        Grid.SetColumn(labels, 1); identity.Children.Add(labels);
        Grid.SetColumnSpan(identity, 2); head.Children.Add(identity);
        // No On/Off words: the switch alone, so the row keeps its width for the name and the status.
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -6, 0) };
        _theme.Accentuate(toggle.Resources);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, definition.Name);
        toggle.Toggled += (_, _) => { if (!_syncingAgentSwitches) SetAgentEnabled(id, toggle.IsOn); };
        Grid.SetColumn(toggle, 2); head.Children.Add(toggle);
        var more = _theme.IconButton("", "Details", 28, 12);
        more.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(more, 3); head.Children.Add(more);

        var details = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 4, 2), Visibility = Visibility.Collapsed };
        var where = _theme.MonoText(""); where.TextWrapping = TextWrapping.Wrap; where.TextTrimming = TextTrimming.None;
        details.Children.Add(where);
        var executable = new TextBox { Header = "Executable or PowerShell command", Text = definition.Executable, FontFamily = Theme.Mono };
        var arguments = new TextBox { Header = "Extra arguments · JSON string array", Text = JsonSerializer.Serialize(definition.Arguments), FontFamily = Theme.Mono };
        var history = new TextBox { Header = "Authoritative history home", Text = definition.HistoryDirectory, FontFamily = Theme.Mono };
        // Empty is Automatic: the tool's own icon where SIC knows it, else the letter tile. Browse fills in a file's path; Save keeps it.
        var icon = new TextBox { Header = "Icon location", Text = definition.Icon ?? "", FontFamily = Theme.Mono, PlaceholderText = "Automatic" };
        var browse = Theme.TextButton("Browse", ".exe, .ico or .png");
        browse.VerticalAlignment = VerticalAlignment.Bottom;
        browse.Click += async (_, _) => { if (await PickIconAsync() is { } picked) icon.Text = picked; };
        var iconRow = new Grid { ColumnSpacing = 8 };
        iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        iconRow.Children.Add(icon);
        Grid.SetColumn(browse, 1); iconRow.Children.Add(browse);
        details.Children.Add(executable); details.Children.Add(arguments); details.Children.Add(history); details.Children.Add(iconRow);
        var error = new TextBlock { Foreground = _theme.Danger, TextWrapping = TextWrapping.Wrap, FontFamily = Theme.Body, FontSize = 12, Visibility = Visibility.Collapsed };
        var save = Theme.TextButton("Save " + definition.Name);
        save.Click += async (_, _) =>
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(arguments.Text) ?? throw new InvalidDataException("Arguments must be a JSON string array.");
                if (parsed.Any(argument => argument is null) || string.IsNullOrWhiteSpace(executable.Text) || !Path.IsPathFullyQualified(history.Text.Trim()))
                    throw new InvalidDataException("An agent needs an executable, string arguments, and an absolute history home.");
                var chosenIcon = icon.Text.Trim().Trim('"');
                if (chosenIcon.Length > 0 && (!AgentIcons.Usable(chosenIcon) || !File.Exists(chosenIcon)))
                    throw new InvalidDataException("The icon must be an .exe, .ico or .png file that exists, or empty for Automatic.");
                // The agent as it is now, not as the panel was built: its switch may have changed since.
                var revised = _preferences.Agents.First(agent => agent.Id == id) with
                {
                    Executable = executable.Text.Trim(), Arguments = parsed, HistoryDirectory = history.Text.Trim(), Icon = chosenIcon.Length > 0 ? chosenIcon : null
                };
                AgentLaunchBuilder.HistoryVariable(revised);
                error.Visibility = Visibility.Collapsed;
                _preferences.Agents = _preferences.Agents.Select(agent => agent.Id == id ? revised : agent).ToList();
                SavePreferences();
                await Task.WhenAll(DetectToolsAsync(), RefreshHistoryAsync());
                UpdateStatus(definition.Name + " settings saved");
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { error.Text = exception.Message; error.Visibility = Visibility.Visible; }
            catch (Exception exception) { ShowError(exception.Message); }
        };
        details.Children.Add(save); details.Children.Add(error);
        more.Click += (_, _) =>
        {
            var open = details.Visibility != Visibility.Visible;
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            more.Content = Theme.Icon(open ? "" : "", 12, _theme.TextSecondary);
            ToolTipService.SetToolTip(more, open ? "Hide details" : "Details");
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(head);
        Grid.SetRow(details, 1); body.Children.Add(details);
        _agentRows[id] = new(toggle, identity, status, where);
        return new Border
        {
            Child = body, Background = _theme.Layer, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 6, 8)
        };
    }

    /// <summary>The agents' icons SIC ships beside its exe: OpenCode's logo, and the letter tiles for File Explorer's menu.</summary>
    internal static string BundledIcons => Path.Combine(AppContext.BaseDirectory, "Assets", "icons");

    /// <summary>Settings → Agents → Browse beside Icon location: an .exe, .ico or .png, or null when nothing was chosen.</summary>
    private async Task<string?> PickIconAsync()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
        foreach (var extension in AgentIcons.Extensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    /// <summary>Each agent's tile source as the last refresh found it (docs/decisions/agent-icons.md); null for the letter tile.</summary>
    private readonly Dictionary<string, AgentIconSource?> _agentIconSources = new(StringComparer.Ordinal);

    /// <summary>
    /// After detection: each agent's own icon read again where its source or that file changed, every tile of those agents
    /// redrawn, and the Explorer menu brought in line. An agent whose icon cannot be read keeps its letter tile; nothing here throws.
    /// </summary>
    private async Task RefreshAgentIconsAsync()
    {
        if (_theme.AgentIcons is not { } cache) return;
        var changed = new List<string>();
        // Codex's desktop app, from the Store: its package list is slow to read, so it is read once, off the UI thread.
        var codexApp = await Task.Run(() => StoreAppLogos.Find(AgentIcons.CodexPackage));
        foreach (var agent in _preferences.Agents)
        {
            var source = AgentIcons.Source(agent, _detection.GetValueOrDefault(agent.Id)?.Path, File.Exists, name => name == AgentIcons.CodexPackage ? codexApp : null, BundledIcons);
            _agentIconSources[agent.Id] = source;
            try { if (await cache.UpdateAsync(agent.Id, source)) changed.Add(agent.Id); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("agent icon " + agent.Id + ": " + exception.Message); }
        }
        if (changed.Count > 0) _theme.RefreshAgentTiles(changed);
        await cache.UpdateMenuLibraryAsync(_preferences.Agents.Select(agent => agent.Id));
        RefreshAgentRows();
        SyncExplorerMenu();
    }

    /// <summary>The source of an agent's tile for the Explorer menu: the last refresh's, or before one, what the cache last read.</summary>
    private AgentIconSource? IconSourceFor(string id)
        => _agentIconSources.TryGetValue(id, out var source) ? source : _theme.AgentIcons?.CachedSource(id);

    /// <summary>The agent's icon in the menu's icon library (<see cref="AgentIconCache.MenuIcon"/>), or null.</summary>
    private string? LibraryIconFor(string id) => _theme.AgentIcons?.MenuIcon(id);

    /// <summary>Settings → Agents, an agent's switch, or a placeholder's Turn on. Saved and applied at once.</summary>
    internal void SetAgentEnabled(string id, bool on)
    {
        if (_preferences.Agents.FirstOrDefault(agent => agent.Id == id) is not { } current || current.Enabled == on) return;
        _preferences.Agents = _preferences.Agents.Select(agent => agent.Id == id ? agent with { Enabled = on } : agent).ToList();
        SavePreferences();
        SyncExplorerMenu();
        _ = ApplyAgentSwitchesAsync(refreshHistory: on);
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _environmentSettle;

    /// <summary>
    /// Windows said the user's or the PC's variables changed (TrayIcon), as an installer does once it has added its folder to
    /// Path: detection looks again once two seconds pass without another such message, since an installer can send several.
    /// </summary>
    private void EnvironmentChanged()
    {
        if (_closing) return;
        if (_environmentSettle is null)
        {
            _environmentSettle = DispatcherQueue.CreateTimer();
            _environmentSettle.Interval = TimeSpan.FromSeconds(2);
            _environmentSettle.IsRepeating = false;
            _environmentSettle.Tick += async (_, _) =>
            {
                if (_closing) return;
                Diagnostics.Log("environment changed: detecting again");
                await DetectToolsAsync();
            };
        }
        _environmentSettle.Stop();
        _environmentSettle.Start();
    }

    /// <summary>How long after a detection opening New session does not start another (<see cref="DetectForNewSession"/>).</summary>
    private static readonly TimeSpan DetectionRest = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether opening New session should look for agents again: some agent is undecided, so it may have been installed since
    /// (by an installer that did not say the environment changed, or by copying it onto Path), and no detection ran in the
    /// last minute. An agent switched off is the user's, so with none undecided the probe, a PowerShell with its profile, is
    /// not started at all.
    /// </summary>
    private bool DetectForNewSession => !_closing && _preferences.Agents.Any(agent => agent.Enabled is null) && DateTimeOffset.UtcNow - _detectedAt > DetectionRest;

    /// <summary>"Claude Code", "Claude Code and Codex", "Hermes, Claude Code and Codex".</summary>
    private static string Names(IReadOnlyList<string> names)
        => names.Count < 2 ? string.Concat(names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    /// <summary>Whether the agent is switched on; PowerShell always is.</summary>
    private bool AgentOn(string id) => id == "shell" || _preferences.Agents.Any(agent => agent.Id == id && agent.IsOn);

    /// <summary>The agents switched off, or still undecided, whose conversations Sessions leaves out.</summary>
    private string[] HiddenAgents() => _preferences.Agents.Where(agent => !agent.IsOn).Select(agent => agent.Id).ToArray();

    /// <summary>
    /// After a switch changed, by hand or by detection: the cards, the Sessions filter, the placeholders and Resume all follow.
    /// An agent turned on has its history read again, since nothing was read while it was off; otherwise the list is redrawn.
    /// </summary>
    private async Task ApplyAgentSwitchesAsync(bool refreshHistory)
    {
        RefreshAgentRows();
        RefreshAgentFilterBar();
        foreach (var pane in AllPanes.ToArray()) pane.RefreshPreview();
        UpdateStatus();
        // After the refresh under way, if any: on a first run detection turns the agents on while the refresh started beside it,
        // which read none of them, may still be running, and joining that one would read nothing.
        try { await (refreshHistory ? RefreshHistoryAfterCurrentAsync() : SearchAsync()); }
        catch (Exception exception) when (!_closing) { ShowError(exception.Message); }
    }

    /// <summary>The Sessions filter menu offers the agents that are on; a filter on one switched off goes back to every agent (the caller searches again).</summary>
    private void RefreshAgentFilterBar()
    {
        foreach (var (id, item) in _agentItems)
            if (id.Length > 0) item.Visibility = AgentOn(id) ? Visibility.Visible : Visibility.Collapsed;
        if (_preferences.AgentFilter is { } filter && !AgentOn(filter)) { _preferences.AgentFilter = null; SavePreferences(); }
        RefreshAgentFilter();
    }

    /// <summary>Puts each card's switch, status and found-at line, and the summary, in step with the settings and the last detection.</summary>
    private void RefreshAgentRows()
    {
        if (_agentSummary is null) return;
        _syncingAgentSwitches = true;
        try
        {
            foreach (var agent in _preferences.Agents)
            {
                if (!_agentRows.TryGetValue(agent.Id, out var row)) continue;
                row.Switch.IsOn = agent.IsOn;
                row.Identity.Opacity = agent.IsOn ? 1 : 0.6;
                var found = _detection.GetValueOrDefault(agent.Id);
                (row.Status.Text, row.Status.Foreground) = found is null
                    ? _detecting || !_detectionFailed ? ("Checking…", _theme.TextTertiary) : ("Detection unavailable", _theme.Warning)
                    : found.Available ? ("Detected", _theme.Success) : ("Not found", _theme.Warning);
                row.Where.Text = found is null ? "" : found.Describe();
                if (_agentIconSources.TryGetValue(agent.Id, out var iconSource))
                    row.Where.Text += (row.Where.Text.Length == 0 ? "" : "\n") + (iconSource is null ? "Icon: SicTerm's letter tile."
                        : "Icon from " + iconSource.Path + (iconSource.Plate is null ? "" : ", on its tile colour #" + iconSource.Plate));
                row.Where.Visibility = row.Where.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        finally { _syncingAgentSwitches = false; }
        var on = _preferences.Agents.Count(agent => agent.IsOn);
        _agentSummary.Text = $"{on} of {_preferences.Agents.Count} on" + (_detection.Count == 0 ? "" : $" · {_detection.Values.Count(found => found.Available)} detected");
    }
}
