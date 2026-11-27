using System.Diagnostics;
using System.Text.Json;
using Sic.Core.Appearance;
using Sic.Core.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Getting a PC set up (<see cref="SetupCatalog"/>; November 27, 2026), for one that has no PowerShell 7, no coding agent, or
/// neither. A Store install on a fresh Windows 11 has neither, and all SicTerm said was a notice over five links.
/// <para>
/// The Get set up panel is the home screen's centre while something is missing, until its X closes it there: PowerShell 7
/// under one label and the four agents under another, each row with its name, whether it was found, one line on what it is
/// and Install. Settings → Agents has the same Install on each agent card that was not found, and PowerShell has a card of its
/// own in a Shell section there.
/// </para>
/// <para>
/// Install opens a popup with the longer text and the vendor's own command; its Install closes the popup and opens a PowerShell
/// tab with the command typed at its prompt, not run: the person presses Enter and answers the installer themselves. SicTerm
/// then watches for the program, and once the tab's output has stopped the notice says it is installed, with Close tab and,
/// for an agent, Start a session. Claude Code's installer leaves its folder off Path, so its notice and its row also offer Add
/// to PATH (<see cref="UserPath"/>); SicTerm finds it there either way (EnvironmentRefresh).
/// </para>
/// <para>
/// The launch notice is one sentence with a Set up button, shown at each launch until its X is clicked, and not while the
/// panel is on screen saying the same. Another notice that takes its place only borrows it: when that one goes, it comes back.
/// </para>
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The gaps the launch notice shows while it is the notice up (<see cref="_setupNoticeGeneration"/>), which its X closes.</summary>
    private SetupGap _setupNoticeGaps;
    private int _setupNoticeGeneration = -1;
    /// <summary>The launch notice is waiting for the welcome to close, or for a notice already up to go.</summary>
    private bool _setupNoticeDue;

    /// <summary>The PC's gaps in place of the real ones, for the smoke; null in use.</summary>
    internal SetupGap? SetupGapsOverride { get; set; }

    /// <summary>
    /// What this PC lacks: PowerShell 7 when the shell SicTerm would start is not pwsh.exe (the MSI's or the MSIX's), and an agent
    /// when the last detection found none of the built-in agents; no agent gap while no detection has answered or the last failed.
    /// </summary>
    internal SetupGap SetupGaps
    {
        get
        {
            if (SetupGapsOverride is { } forced) return forced;
            string? shell = null;
            try { shell = AgentDetection.PowerShellPath; }
            catch (FileNotFoundException) { }
            bool? agentFound = _detectionFailed || _detection.Count == 0 ? null : _detection.Values.Any(found => found.Available);
            return SetupAdvisor.Gaps(SetupAdvisor.IsPowerShell7(shell), agentFound);
        }
    }

    // ---- The launch notice ----

    /// <summary>Whether the window's notice is the setup notice now; for verification.</summary>
    internal bool SetupNoticeShown => _notice.IsOpen && _noticeGeneration == _setupNoticeGeneration && _setupNoticeGaps != SetupGap.None;

    /// <summary>The notice's own X, and its close by anything else: only the X closes its gaps, and a close lets a waiting notice show.</summary>
    private void WatchSetupNotice()
    {
        // The window has loaded: from here the home panel may be built (RefreshSetupRows).
        _setupReady = true;
        _notice.CloseButtonClick += (_, _) => CloseSetupNotice();
        _notice.Closed += (_, _) =>
        {
            if (_setupNoticeDue && _welcomeFilm is null) DispatcherQueue.TryEnqueue(ShowSetupNotice);
        };
    }

    /// <summary>
    /// The launch's notice, once detection has answered: not in the smoke, and not on a launch Open in SicTerm asked for, which
    /// has a dialog or a session to show (the next launch has the notice). It waits for the welcome, and for a notice already up.
    /// </summary>
    private void ShowSetupNoticeAtLaunch()
    {
        if (_options.SmokeTest || _options.Open is not null || _closing) return;
        _setupNoticeDue = true;
        if (_welcomeFilm is null && !_notice.IsOpen) ShowSetupNotice();
    }

    /// <summary>Shows the launch notice for the gaps not closed at launch, when there are any and the panel is not on screen with them.</summary>
    internal void ShowSetupNotice()
    {
        if (_closing || _welcomeFilm is not null) return;
        // A notice that came up meanwhile is left alone; this one follows when it goes.
        if (_notice.IsOpen && !SetupNoticeShown) return;
        _setupNoticeDue = false;
        var gaps = SetupAdvisor.Open(SetupGaps, _preferences.SetupClosedAtLaunch);
        if (SetupHomeShown || SetupAdvisor.For(gaps) is not { } advice)
        {
            if (SetupNoticeShown) _notice.IsOpen = false;
            _setupNoticeGaps = SetupGap.None;
            return;
        }
        var setUp = Theme.TextButton("Set up");
        setUp.Click += (_, _) => OpenSetup();
        ShowNotice(InfoBarSeverity.Informational, "", advice.Message, setUp, brief: false);
        _setupNoticeGaps = gaps;
        _setupNoticeGeneration = _noticeGeneration;
        // ShowNotice marks a setup notice it replaces as due again, which this one, its own update, is not.
        _setupNoticeDue = false;
    }

    /// <summary>The launch notice's X: its gaps are not shown at launch again.</summary>
    internal void CloseSetupNotice()
    {
        if (_noticeGeneration != _setupNoticeGeneration || _setupNoticeGaps == SetupGap.None) return;
        _preferences.SetupClosedAtLaunch |= _setupNoticeGaps;
        _setupNoticeGaps = SetupGap.None;
        SavePreferences();
    }

    /// <summary>
    /// Set up, from the launch notice or New session: the panel on the home screen when that is what the window shows, back
    /// from its X for this run; otherwise Settings → Agents, where each item missing has its Install. The notice goes without
    /// its X being counted.
    /// </summary>
    internal void OpenSetup()
    {
        if (SetupNoticeShown) { _setupNoticeGaps = SetupGap.None; _notice.IsOpen = false; }
        if (_open.Count == 0)
        {
            _setupReopened = true;
            RefreshSetupRows();
            return;
        }
        OpenPanel("settings");
        ShowSettingsTab(SettingsTabs.Agents);
    }

    // ---- The home panel ----

    /// <summary>One item's row in the home panel: its name, whether it was found, one line, and its buttons.</summary>
    private sealed class SetupRow
    {
        internal required SetupItem Item { get; init; }
        internal required FrameworkElement Root { get; init; }
        internal required TextBlock Status { get; init; }
        internal required TextBlock Line { get; init; }
        internal required Button Install { get; init; }
        internal required Button Start { get; init; }
        internal required Button AddPath { get; init; }
        internal Border? Divider { get; set; }
    }

    private readonly List<SetupRow> _setupRows = [];
    /// <summary>Where the panel goes on the home screen (BuildWelcome), and what makes room for it there.</summary>
    private Border? _setupHomeSlot;
    private FrameworkElement? _homeMark, _homeTagline;
    private StackPanel? _homeStack;
    private Border? _setupPanel;
    /// <summary>Set up was asked for in this run, so the home panel shows whatever its X closed before.</summary>
    private bool _setupReopened;
    /// <summary>
    /// The window has loaded. Until then the home panel is not built: it would be on screen while the window loads, and agent
    /// tiles shown that early have ended SicTerm before (OpenPanel's note on Settings restored at launch).
    /// </summary>
    private bool _setupReady;

    /// <summary>Whether the home screen is what the window shows and the panel is on it; the launch notice stays away then.</summary>
    internal bool SetupHomeShown => _open.Count == 0 && _setupPanel is { Visibility: Visibility.Visible };

    /// <summary>The home panel's rows, for verification: each item's status, line and the buttons on show.</summary>
    internal IReadOnlyList<(string Id, bool Visible, string Status, string Line, IReadOnlyList<string> Buttons)> SetupRowFacts() =>
        _setupRows.Select(row => (row.Item.Id, row.Root.Visibility == Visibility.Visible, row.Status.Text, row.Line.Text,
            (IReadOnlyList<string>)new[] { row.Start, row.AddPath, row.Install }.Where(button => button.Visibility == Visibility.Visible)
                .Select(button => button.Content as string ?? "").ToList())).ToList();

    /// <summary>Whether <paramref name="item"/> is on this PC: PowerShell 7 by the shell SicTerm would start, Node.js by npm on Path, an agent by the last detection.</summary>
    private bool SetupFound(SetupItem item) => SetupFound(item, SetupGaps);

    private bool SetupFound(SetupItem item, SetupGap gaps)
    {
        if (item.Id == SetupCatalog.PowerShell) return !gaps.HasFlag(SetupGap.PowerShell);
        if (item.Id == SetupCatalog.Node.Id) return SetupNpm;
        // The smoke's stand-in for a PC without agents: none of them counts.
        if (SetupGapsOverride is { } forced && forced.HasFlag(SetupGap.Agent)) return false;
        return _detection.GetValueOrDefault(item.Id)?.Available == true;
    }

    private static string ClaudeFolder => SetupCatalog.ClaudeFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The folder's state in place of the real one, for the smoke; null in use.</summary>
    internal bool? ClaudeOffPathOverride { get; set; }

    /// <summary>Whether Claude Code is the one its own installer put under .local\bin, in a folder neither the account's Path nor the PC's names.</summary>
    private bool ClaudeOffPath()
    {
        if (ClaudeOffPathOverride is { } forced) return forced;
        if (_detection.GetValueOrDefault("claude") is not { Available: true, Path: { Length: > 0 } path }) return false;
        try { return path.StartsWith(ClaudeFolder + "\\", StringComparison.OrdinalIgnoreCase) && !UserPath.Contains(ClaudeFolder); }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>The command Install would type for <paramref name="item"/> on this PC, null where the popup offers the install page.</summary>
    private static string? SetupCommand(SetupItem item) => SetupCatalog.Command(item.Id, SetupWinget, SetupNpm);

    private static bool SetupWinget => PathList.Find("winget.exe") is not null;
    private static bool SetupNpm => PathList.Find("npm.cmd") is not null;

    /// <summary>
    /// A row: the tile, the name with whether it was found, one line under it, and at the right Install (which opens the
    /// popup), or Start a session once an agent is found, with Add to PATH beside it for Claude Code off Path. The command, its
    /// Copy and the longer text are in the popup, so the panel stays short (Chris, November 27, 2026).
    /// </summary>
    private SetupRow BuildSetupRow(SetupItem item)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = _theme.AgentTile(item.Id == SetupCatalog.PowerShell ? "shell" : item.Id, 28);
        tile.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(tile);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(_theme.BodyText(item.Name, 14, 600));
        var status = _theme.BodyText("", 12); status.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(status);
        text.Children.Add(head);
        var line = _theme.Caption(item.Line); line.TextWrapping = TextWrapping.Wrap; line.TextTrimming = TextTrimming.None;
        text.Children.Add(line);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var start = Theme.TextButton("Start a session");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(start, "Start a " + item.Name + " session");
        start.Click += async (_, _) => await StartSetupSessionAsync(item.Id);
        var addPath = Theme.TextButton("Add to PATH", "Adds " + ClaudeFolder + " to this account's PATH");
        addPath.Click += (_, _) => AddClaudeToPath();
        var install = _theme.PrimaryButton("Install");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(install, "Install " + item.Name);
        install.Click += async (_, _) => await ShowInstallAsync(item);
        actions.Children.Add(start); actions.Children.Add(addPath); actions.Children.Add(install);
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        return new SetupRow { Item = item, Root = grid, Status = status, Line = line, Install = install, Start = start, AddPath = addPath };
    }

    /// <summary>A group's label in the home panel.</summary>
    private readonly List<(bool Agents, FrameworkElement Header)> _setupHeaders = [];

    /// <summary>The rows in two groups, PowerShell 7 and then the agents, each under its label, a hairline between each two rows of a group.</summary>
    private StackPanel BuildSetupRows()
    {
        var rows = new StackPanel();
        foreach (var agents in new[] { false, true })
        {
            var header = _theme.Eyebrow(agents ? SetupCatalog.AgentsLabel : SetupCatalog.ShellLabel);
            header.Margin = new Thickness(0, agents ? 16 : 4, 0, 0);
            rows.Children.Add(header);
            _setupHeaders.Add((agents, header));
            foreach (var item in SetupCatalog.Items.Where(item => (item.Id != SetupCatalog.PowerShell) == agents))
            {
                var row = BuildSetupRow(item);
                row.Divider = _theme.Divider();
                rows.Children.Add(row.Divider);
                rows.Children.Add(row.Root);
                _setupRows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>
    /// The home screen's panel, built when it first has something to show: agent tiles drawn while the window was still loading
    /// have ended SicTerm before (OpenPanel's note on Settings restored at launch).
    /// </summary>
    private Border BuildSetupPanel()
    {
        var content = new StackPanel { Spacing = 6 };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(_theme.Heading(SetupCatalog.Heading, 18));
        var close = _theme.IconButton(((char)0xE711).ToString(), "Close", 28, 12);
        close.Click += (_, _) => CloseSetupPanel();
        Grid.SetColumn(close, 1); top.Children.Add(close);
        content.Children.Add(top);
        content.Children.Add(BuildSetupRows());
        content.Children.Add(_theme.Divider());
        var footnote = _theme.Tertiary(SetupCatalog.Footnote); footnote.TextWrapping = TextWrapping.Wrap; footnote.TextTrimming = TextTrimming.None;
        footnote.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(footnote);
        return new Border
        {
            Child = content, Background = _theme.Layer, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 14, 16, 14), Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    /// <summary>The home panel's X: it stays away for the gaps it showed, and comes back for one that was not among them, or when Set up is clicked.</summary>
    internal void CloseSetupPanel()
    {
        _preferences.SetupClosedOnHome |= SetupGaps;
        _setupReopened = false;
        SavePreferences();
        RefreshSetupRows();
    }

    /// <summary>Everything in step with the PC, after each detection: the notice, the panel, the agent cards' Install and the PowerShell card.</summary>
    internal void RefreshSetupAdvice()
    {
        RefreshSetupRows();
        RefreshAgentRows();
        if (SetupNoticeShown && (SetupHomeShown || SetupAdvisor.Open(SetupGaps, _preferences.SetupClosedAtLaunch) != _setupNoticeGaps)) ShowSetupNotice();
    }

    /// <summary>Shows or hides the home panel and brings its rows and the PowerShell card up to date.</summary>
    internal void RefreshSetupRows()
    {
        var gaps = SetupGaps;
        var home = _setupReady && _setupHomeSlot is not null && gaps != SetupGap.None && (_setupReopened || SetupAdvisor.Open(gaps, _preferences.SetupClosedOnHome) != SetupGap.None);
        if (home && _setupPanel is null && !_closing) { _setupPanel = BuildSetupPanel(); _setupHomeSlot!.Child = _setupPanel; }
        if (_setupPanel is { } panel)
        {
            panel.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            // The mark and the tagline give their room to the panel, and the column widens for it.
            if (_homeMark is not null) _homeMark.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (_homeTagline is not null) _homeTagline.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (_homeStack is not null) _homeStack.MaxWidth = home ? 640 : 480;
        }
        var checking = _detection.Count == 0 && !_detectionFailed && SetupGapsOverride is null;
        var offPath = ClaudeOffPath();
        foreach (var group in _setupRows.GroupBy(row => row.Item.Id != SetupCatalog.PowerShell))
        {
            var first = true;
            foreach (var row in group)
            {
                var agent = row.Item.Id != SetupCatalog.PowerShell;
                var found = SetupFound(row.Item, gaps);
                var needsPath = found && row.Item.Id == "claude" && offPath;
                (row.Status.Text, row.Status.Foreground) = found ? ("Detected", _theme.Success) : agent && checking ? ("Checking…", _theme.TextTertiary) : ("Not found", _theme.TextTertiary);
                row.Line.Text = needsPath ? SetupCatalog.ClaudeOffPath : row.Item.Line;
                row.Install.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
                row.Start.Visibility = found && agent ? Visibility.Visible : Visibility.Collapsed;
                row.AddPath.Visibility = needsPath ? Visibility.Visible : Visibility.Collapsed;
                // No hairline above a group's first row: its label is there.
                if (row.Divider is { } divider) divider.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
                first = false;
            }
        }
        RefreshShellCard();
    }

    private void OpenSetupPage(SetupItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.Page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Diagnostics.Log("setup page " + item.Page + ": " + exception.Message);
            ShowError("The page did not open: " + item.Page.AbsoluteUri);
        }
    }

    // ---- The install popup ----

    /// <summary>The install popup while it is up, and the item it is for; for verification.</summary>
    internal (ContentDialog Dialog, string Id, string? Command, string Primary)? InstallPopup { get; private set; }

    /// <summary>
    /// Install, from the home panel or an agent's card in Settings: a popup with the line under the name, the longer text, the
    /// command with Copy, and Install, which closes the popup and then opens the tab (Chris, November 27, 2026). Where this PC
    /// cannot run the command (no winget for PowerShell 7) the popup's button opens the vendor's page instead. OpenCode without
    /// npm leads with Node.js, which installs for all users, so Windows asks for admin rights as its installer starts, in the
    /// tab, where SicTerm can still watch it; nothing SicTerm runs is elevated.
    /// </summary>
    internal async Task ShowInstallAsync(SetupItem item)
    {
        if (_dialogsOpen > 0) return;
        var needsNode = item.Id == "opencode" && !SetupNpm;
        var step = needsNode ? SetupCatalog.Node : item;
        var command = SetupCommand(step);
        var content = new StackPanel { Spacing = 10, MinWidth = 460, MaxWidth = 560 };
        var line = _theme.BodyText(item.Line, 13, 600); line.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(line);
        var detail = _theme.BodyText(item.Detail, 13); detail.TextWrapping = TextWrapping.Wrap; detail.Foreground = _theme.TextSecondary;
        content.Children.Add(detail);
        if (needsNode)
        {
            var node = _theme.BodyText(SetupCatalog.OpenCodeNeedsNode, 13); node.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(node);
        }
        if (command is not null)
        {
            content.Children.Add(_theme.Eyebrow(needsNode ? "Node.js command" : "Command"));
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = _theme.MonoText(command); text.TextWrapping = TextWrapping.Wrap; text.TextTrimming = TextTrimming.None; text.IsTextSelectionEnabled = true; text.Foreground = _theme.Text;
            row.Children.Add(new Border
            {
                Child = text, Background = _theme.Base, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 7)
            });
            var copy = _theme.IconButton(((char)0xE8C8).ToString(), "Copy the command", 32, 14);
            copy.VerticalAlignment = VerticalAlignment.Top;
            copy.Click += (_, _) =>
            {
                try { ClipboardWrite.SetText(command); UpdateStatus(step.Name + " install command copied"); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { ShowError("The command was not copied: " + exception.Message); }
            };
            Grid.SetColumn(copy, 1); row.Children.Add(copy);
            content.Children.Add(row);
            var footnote = _theme.Tertiary(needsNode ? SetupCatalog.NodeFootnote : SetupCatalog.PopupFootnote); footnote.TextWrapping = TextWrapping.Wrap; footnote.TextTrimming = TextTrimming.None;
            content.Children.Add(footnote);
        }
        var primary = command is null ? (needsNode ? "Node.js install page" : "Install page") : needsNode ? "Install Node.js" : "Install";
        var dialog = Dialog("Install " + item.Name, content, primary);
        // The vendor's page beside the command, for whoever would rather read it first; with no command the page is the main button.
        if (command is not null) dialog.SecondaryButtonText = needsNode ? "OpenCode's page" : "Install page";
        InstallPopup = (dialog, item.Id, command, primary);
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { InstallPopup = null; }
        if (result == ContentDialogResult.Secondary) OpenSetupPage(item);
        else if (result == ContentDialogResult.Primary)
        {
            if (command is null) OpenSetupPage(step);
            else await InstallAsync(step, command);
        }
    }

    // ---- Install ----

    /// <summary>An Install under way: the tab it typed into, and what its watch has seen.</summary>
    private sealed class SetupInstall
    {
        internal required SetupItem Item { get; init; }
        internal required SessionPane Pane { get; init; }
        internal DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        internal int Ticks { get; set; }
        internal bool ProgramSeen { get; set; }
        internal string? Screen { get; set; }
        internal int Settling { get; set; }
    }

    private readonly List<SetupInstall> _installs = [];
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _installWatch;
    private bool _installTicking;

    /// <summary>The installs being watched, by item; for verification.</summary>
    internal IReadOnlyList<string> SetupInstalls => _installs.Select(install => install.Item.Id).ToList();

    /// <summary>
    /// The popup's Install: a PowerShell tab named for the item, in the profile folder, with the command typed at its prompt once
    /// the prompt is there, and not run. Text sent while the pane was still starting was lost in a Windows Sandbox run, so the
    /// typing waits for the prompt (<see cref="ShellPrompt"/>), or for the screen to stop changing under a profile's own prompt.
    /// </summary>
    internal async Task InstallAsync(SetupItem item, string command)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (await StartNewAsync("shell", folder, title: "Install " + item.Name) is not { } pane) return;
            _installs.RemoveAll(install => install.Item.Id == item.Id);
            _installs.Add(new SetupInstall { Item = item, Pane = pane });
            WatchInstalls();
            await PromptShownAsync(pane);
            if (_closing || !AllPanes.Contains(pane) || !pane.IsRunning) return;
            await pane.PasteTextAsync(command);
            Diagnostics.Log("setup: typed the install command for " + item.Id);
            Notify(InfoBarSeverity.Informational, "Install " + item.Name, SetupCatalog.Typed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ShowError(exception.Message); }
    }

    /// <summary>Waits, twenty seconds at most, until the pane shows PowerShell's prompt, or its screen has held still for a second and a half.</summary>
    private async Task PromptShownAsync(SessionPane pane)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        string? last = null;
        var still = 0;
        while (DateTimeOffset.UtcNow < until && !_closing && AllPanes.Contains(pane))
        {
            await Task.Delay(300);
            if (pane.Status is PaneStatus.Exited or PaneStatus.Failed) return;
            if (pane.Terminal is not { } terminal || !pane.IsRunning) continue;
            string screen;
            try { screen = await terminal.ReadScreenAsync(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { continue; }
            if (ShellPrompt.Ready(screen)) return;
            still = screen.Trim().Length > 0 && screen == last ? still + 1 : 0;
            if (still >= 5) return;
            last = screen;
        }
    }

    private void WatchInstalls()
    {
        if (_installWatch is null)
        {
            _installWatch = DispatcherQueue.CreateTimer();
            _installWatch.Interval = TimeSpan.FromSeconds(2);
            _installWatch.Tick += async (_, _) => await InstallTickAsync();
        }
        _installWatch.Start();
    }

    /// <summary>How long after Install a tab left open still has detection look again for a program that says nothing when it lands.</summary>
    private static readonly TimeSpan InstallWatch = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Every two seconds while an install is watched. Not yet found: the program in its known place (Claude Code and Codex), or
    /// PowerShell 7 anywhere, is looked for at once, and detection looks again when it appears, and every sixteen seconds for
    /// the installers with no known place (Hermes, npm), which are also found when they change Path; Node.js is found by npm on
    /// Path, read again from the registry. Found: the notice waits for the tab's screen to stop changing, since claude.exe is in
    /// place a moment before its installer has finished talking. The watch ends when the item is found and said, or its tab has gone.
    /// </summary>
    private async Task InstallTickAsync()
    {
        if (_installTicking || _closing) return;
        _installTicking = true;
        try
        {
            var detect = false;
            // Node.js's installer changes the PC's Path; SicTerm's own follows it here as well as on Windows' word.
            if (_installs.Any(install => install.Item.Id == SetupCatalog.Node.Id)) EnvironmentRefresh.Apply();
            foreach (var install in _installs.ToList())
            {
                var open = AllPanes.Contains(install.Pane);
                install.Ticks++;
                if (!SetupFound(install.Item))
                {
                    if (!open) { _installs.Remove(install); continue; }
                    if (!install.ProgramSeen && SetupProgramThere(install.Item))
                    {
                        install.ProgramSeen = true;
                        if (install.Item.Id == SetupCatalog.PowerShell) RefreshSetupAdvice(); else detect = true;
                    }
                    else if (install.Item.Id is not (SetupCatalog.PowerShell or "node") && install.Ticks % 8 == 0 && DateTimeOffset.UtcNow - install.Started < InstallWatch) detect = true;
                    continue;
                }
                if (open && install.Settling < 30 && install.Pane.Terminal is { } terminal && install.Pane.IsRunning)
                {
                    string? screen = null;
                    try { screen = await terminal.ReadScreenAsync(); }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { }
                    install.Settling++;
                    if (screen is not null && screen != install.Screen) { install.Screen = screen; continue; }
                }
                _installs.Remove(install);
                ShowInstalled(install.Item, open ? install.Pane : null);
            }
            if (detect && !_detecting) await DetectToolsAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("setup install watch: " + exception.Message); }
        finally
        {
            _installTicking = false;
            if (_installs.Count == 0) _installWatch?.Stop();
        }
    }

    /// <summary>Whether the item's program is where its installer puts it, before detection has looked.</summary>
    private static bool SetupProgramThere(SetupItem item)
    {
        if (item.Id == SetupCatalog.PowerShell) return PowerShellLocator.FindPowerShell7() is not null;
        return SetupCatalog.KnownProgram(item.Id, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) is { } program && File.Exists(program);
    }

    /// <summary>
    /// The notice once an install is found, with Close tab for the install's tab while it is open (nothing closes it by itself:
    /// PowerShell 7 is found before winget has finished, and a failure is only explained there). An agent adds Start a session,
    /// which closes the tab too, and says where the other agents are; Claude Code off Path adds Add to PATH; Node.js leads on to
    /// OpenCode's popup.
    /// </summary>
    internal void ShowInstalled(SetupItem item, SessionPane? pane)
    {
        if (_closing) return;
        RefreshSetupAdvice();
        var offPath = item.Id == "claude" && ClaudeOffPath();
        var message = item.Id == SetupCatalog.PowerShell ? SetupCatalog.PowerShellInstalled
            : item.Id == SetupCatalog.Node.Id ? SetupCatalog.NodeInstalled
            : offPath ? SetupCatalog.ClaudeOffPathNotice : SetupCatalog.AgentInstalled(item.Name);
        Diagnostics.Log("setup: " + item.Id + " installed" + (offPath ? ", its folder off Path" : ""));
        var actions = InstalledActions(item, offPath, pane);
        ShowNotice(InfoBarSeverity.Success, "", message, null, brief: false, content: actions.Children.Count == 0 ? null : actions);
    }

    private StackPanel InstalledActions(SetupItem item, bool offPath, SessionPane? pane)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        if (offPath)
        {
            var add = Theme.TextButton("Add to PATH", "Adds " + ClaudeFolder + " to this account's PATH");
            add.Click += (_, _) => AddClaudeToPath(pane);
            actions.Children.Add(add);
        }
        if (item.Id == SetupCatalog.Node.Id && SetupCatalog.Item("opencode") is { } openCode)
        {
            var next = Theme.TextButton("Install OpenCode");
            next.Click += async (_, _) => { await CloseInstallTabAsync(pane); await ShowInstallAsync(openCode); };
            actions.Children.Add(next);
        }
        else if (item.Id is not (SetupCatalog.PowerShell or "node"))
        {
            var start = Theme.TextButton("Start a session");
            start.Click += async (_, _) => await StartSetupSessionAsync(item.Id, pane);
            actions.Children.Add(start);
        }
        if (pane is not null)
        {
            var close = Theme.TextButton("Close tab");
            close.Click += async (_, _) => await CloseInstallTabAsync(pane);
            actions.Children.Add(close);
        }
        return actions;
    }

    /// <summary>Closes an install's tab without asking, its notice with it: the tab is back at its prompt, with nothing to exit cleanly.</summary>
    private async Task CloseInstallTabAsync(SessionPane? pane)
    {
        if (_notice.IsOpen && LastNotice is { Severity: InfoBarSeverity.Success }) _notice.IsOpen = false;
        if (pane is not null && HostOf(pane) is { } host) await CloseTabAsync(host.Item, decided: true);
    }

    /// <summary>The key Add to PATH writes under, in place of the account's own, for the smoke; null in use.</summary>
    internal string? UserPathKeyOverride { get; set; }

    /// <summary>
    /// Add to PATH: Claude Code's folder goes on the end of the account's Path (<see cref="UserPath"/>), and the notice says
    /// so, with Start a session and Close tab still there. Tabs already open keep the Path they started with; new ones have it.
    /// </summary>
    internal void AddClaudeToPath(SessionPane? pane = null)
    {
        try
        {
            var added = UserPath.Add(ClaudeFolder, UserPathKeyOverride ?? UserPath.EnvironmentKey);
            Diagnostics.Log("setup: " + ClaudeFolder + (added ? " added to the account's Path" : " was already on the account's Path"));
            if (SetupCatalog.Item("claude") is { } claude)
                ShowNotice(InfoBarSeverity.Success, "", SetupCatalog.ClaudeOnPathNotice, null, brief: false, content: InstalledActions(claude, offPath: false, pane));
            RefreshSetupAdvice();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ShowError("The account's PATH was not changed: " + exception.Message);
        }
    }

    /// <summary>Start a session, from a row, a card or the installed notice: the install's tab closed, then New session with that agent picked, switched on if it was off.</summary>
    private async Task StartSetupSessionAsync(string id, SessionPane? installTab = null)
    {
        if (!AgentOn(id)) SetAgentEnabled(id, true);
        await CloseInstallTabAsync(installTab);
        await ShowNewSessionAsync(agent: id);
    }

    // ---- Settings → Agents → Shell ----

    private TextBlock? _shellStatus, _shellWhere;
    private Button? _shellInstall;
    private TextBox? _shellArguments;

    /// <summary>The PowerShell card's facts, for verification: what it says, whether Install shows, and the arguments box.</summary>
    internal (string Status, string Where, bool Install, string Arguments)? ShellCardFacts =>
        _shellStatus is null ? null : (_shellStatus.Text, _shellWhere!.Text, _shellInstall!.Visibility == Visibility.Visible, _shellArguments!.Text);

    /// <summary>
    /// Settings → Agents, under the agents: PowerShell in a section of its own, a card like an agent's (Chris, November 27,
    /// 2026). It says which PowerShell SicTerm runs in, offers Install while PowerShell 7 is missing, and keeps extra arguments
    /// for PowerShell tabs, which agents do not get.
    /// </summary>
    private void BuildShellSection(StackPanel page)
    {
        page.Children.Add(Section("Shell"));
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = _theme.AgentTile("shell", 24); tile.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(tile);
        var labels = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(_theme.BodyText("PowerShell", 13, 600));
        _shellStatus = _theme.BodyText("", 12);
        labels.Children.Add(_shellStatus);
        Grid.SetColumn(labels, 1); head.Children.Add(labels);
        _shellInstall = _theme.PrimaryButton("Install");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_shellInstall, "Install PowerShell 7");
        _shellInstall.VerticalAlignment = VerticalAlignment.Center;
        _shellInstall.Click += async (_, _) => { if (SetupCatalog.Item(SetupCatalog.PowerShell) is { } item) await ShowInstallAsync(item); };
        Grid.SetColumn(_shellInstall, 2); head.Children.Add(_shellInstall);
        var more = _theme.IconButton(((char)0xE712).ToString(), "Details", 28, 12);
        more.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(more, 3); head.Children.Add(more);

        var details = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 4, 2), Visibility = Visibility.Collapsed };
        _shellWhere = _theme.MonoText(""); _shellWhere.TextWrapping = TextWrapping.Wrap; _shellWhere.TextTrimming = TextTrimming.None;
        details.Children.Add(_shellWhere);
        _shellArguments = new TextBox { Header = "Extra arguments · JSON string array", Text = JsonSerializer.Serialize(_preferences.ShellArguments), FontFamily = Theme.Mono };
        details.Children.Add(_shellArguments);
        var note = _theme.Caption(SetupCatalog.ShellArgumentsNote); note.TextWrapping = TextWrapping.Wrap; note.TextTrimming = TextTrimming.None;
        details.Children.Add(note);
        var error = new TextBlock { Foreground = _theme.Danger, TextWrapping = TextWrapping.Wrap, FontFamily = Theme.Body, FontSize = 12, Visibility = Visibility.Collapsed };
        var save = Theme.TextButton("Save PowerShell");
        save.Click += (_, _) =>
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(_shellArguments.Text) ?? throw new InvalidDataException("Arguments must be a JSON string array.");
                if (parsed.Any(argument => argument is null)) throw new InvalidDataException("Arguments must be a JSON string array.");
                error.Visibility = Visibility.Collapsed;
                _preferences.ShellArguments = parsed;
                SavePreferences();
                UpdateStatus("PowerShell settings saved · new PowerShell tabs use them");
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { error.Text = exception.Message; error.Visibility = Visibility.Visible; }
        };
        details.Children.Add(save); details.Children.Add(error);
        more.Click += (_, _) =>
        {
            var open = details.Visibility != Visibility.Visible;
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            more.Content = Theme.Icon(open ? ((char)0xE70E).ToString() : ((char)0xE712).ToString(), 12, _theme.TextSecondary);
            ToolTipService.SetToolTip(more, open ? "Hide details" : "Details");
        };
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(head);
        Grid.SetRow(details, 1); body.Children.Add(details);
        page.Children.Add(new Border
        {
            Child = body, Background = _theme.Layer, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 6, 8)
        });
        RefreshShellCard();
    }

    /// <summary>The PowerShell card in step with the PC: which PowerShell, its version and where, and Install while 7 is missing.</summary>
    private void RefreshShellCard()
    {
        if (_shellStatus is null || _shellWhere is null || _shellInstall is null) return;
        string? shell = null;
        try { shell = AgentDetection.PowerShellPath; }
        catch (FileNotFoundException) { }
        var seven = !SetupGaps.HasFlag(SetupGap.PowerShell);
        string? version = null;
        try { if (shell is not null && File.Exists(shell)) version = FileVersionInfo.GetVersionInfo(shell).ProductVersion?.Split(' ', '+')[0]; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        (_shellStatus.Text, _shellStatus.Foreground) = seven
            ? ("PowerShell 7" + (version is { Length: > 0 } && SetupGapsOverride is null ? " · " + version : ""), _theme.Success)
            : ("Windows PowerShell 5.1", _theme.Warning);
        _shellWhere.Text = shell is null ? "No PowerShell was found." : "Runs " + shell;
        _shellInstall.Visibility = seven ? Visibility.Collapsed : Visibility.Visible;
    }
}
