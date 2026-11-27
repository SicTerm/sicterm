using System.Diagnostics;
using Sic.Core.Appearance;
using Sic.Core.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Getting a PC set up (<see cref="SetupCatalog"/>; November 27, 2026), for one that has no PowerShell 7, no coding agent, or
/// neither. A Store install on a fresh Windows 11 has neither, and all SicTerm said was a notice over five links.
/// <para>
/// The Get set up panel is the home screen's centre while something is missing, until its X closes it there: one sentence on
/// what a coding agent is, then a row each for PowerShell 7 and the four agents, with what it is, whether it was found, the
/// command that installs it, and "What is this?" for a reader new to all of it. Settings → Agents keeps the rows of whatever
/// is still missing, so another agent can be added later. Nobody is asked whether they are new to this: a row reads in one
/// line for someone who knows what they want, and opens for someone who does not.
/// </para>
/// <para>
/// Install opens a PowerShell tab and types the vendor's own command at its prompt, without running it: the person presses
/// Enter and answers the installer themselves. SicTerm then watches for the program, and once the tab's output has stopped the
/// window's notice says it is installed and offers Start a session. Claude Code's installer leaves its folder off Path, so its
/// notice and its row also offer Add to PATH (<see cref="UserPath"/>); SicTerm finds it there either way (EnvironmentRefresh).
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
    /// from its X for this run; otherwise Settings → Agents with its rows open. The notice goes without its X being counted.
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
        if (_setupSection is { } section)
        {
            section.IsExpanded = true;
            section.StartBringIntoView();
        }
    }

    // ---- The panel and its rows ----

    /// <summary>One item's row, in the home panel or in Settings → Agents.</summary>
    private sealed class SetupRow
    {
        internal required SetupItem Item { get; init; }
        internal required bool InSettings { get; init; }
        internal required FrameworkElement Root { get; init; }
        internal required TextBlock Status { get; init; }
        internal required TextBlock Line { get; init; }
        internal required FrameworkElement CommandBox { get; init; }
        internal required TextBlock Command { get; init; }
        internal required TextBlock Detail { get; init; }
        internal required Button Install { get; init; }
        internal required Button Copy { get; init; }
        internal required Button Page { get; init; }
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
    private Expander? _setupSection;
    /// <summary>Set up was asked for in this run, so the home panel shows whatever its X closed before.</summary>
    private bool _setupReopened;
    /// <summary>
    /// The window has loaded. Until then the home panel is not built: it would be on screen while the window loads, and agent
    /// tiles shown that early have ended SicTerm before (OpenPanel's note on Settings restored at launch).
    /// </summary>
    private bool _setupReady;
    /// <summary>Settings → Agents has opened its rows by itself once in this run, for a gap; after that the section is left as it is.</summary>
    private bool _setupSectionOpened;

    /// <summary>Whether the home screen is what the window shows and the panel is on it; the launch notice stays away then.</summary>
    internal bool SetupHomeShown => _open.Count == 0 && _setupPanel is { Visibility: Visibility.Visible };

    /// <summary>The rows, for verification: each item's status, command and the buttons on show, in the home panel or in Settings.</summary>
    internal IReadOnlyList<(string Id, bool Visible, string Status, string Line, string? Command, IReadOnlyList<string> Buttons)> SetupRowFacts(bool inSettings) =>
        _setupRows.Where(row => row.InSettings == inSettings).Select(row => (row.Item.Id, row.Root.Visibility == Visibility.Visible, row.Status.Text, row.Line.Text,
            row.CommandBox.Visibility == Visibility.Visible ? row.Command.Text : null,
            (IReadOnlyList<string>)new[] { (row.Install, row.Install.Content as string ?? "Install"), (row.Copy, "Copy"), (row.Page, "Install page"), (row.Start, "Start a session"), (row.AddPath, "Add to PATH") }
                .Where(button => button.Item1.Visibility == Visibility.Visible).Select(button => button.Item2).ToList())).ToList();

    internal Expander? SetupSection => _setupSection;

    /// <summary>Whether <paramref name="item"/> is on this PC: PowerShell 7 by the shell SicTerm would start, an agent by the last detection.</summary>
    private bool SetupFound(SetupItem item) => SetupFound(item, SetupGaps);

    private bool SetupFound(SetupItem item, SetupGap gaps)
    {
        if (item.Id == SetupCatalog.PowerShell) return !gaps.HasFlag(SetupGap.PowerShell);
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

    /// <summary>The command Install would type for <paramref name="item"/> on this PC, null where the row offers the install page.</summary>
    private static string? SetupCommand(SetupItem item) => SetupCatalog.Command(item.Id, SetupWinget, SetupNpm);

    private static bool SetupWinget => PathList.Find("winget.exe") is not null;
    private static bool SetupNpm => PathList.Find("npm.cmd") is not null;

    /// <summary>
    /// A row. On the home screen it is laid across: the tile, the text and the command, then the buttons at the right. The
    /// Settings panel is some 300 pixels wide, where that crushed the text into a column a few words across and cut the status
    /// off (the smoke's capture, November 27, 2026), so there everything is stacked, the buttons under the command.
    /// </summary>
    private SetupRow BuildSetupRow(SetupItem item, bool inSettings)
    {
        var tile = _theme.AgentTile(item.Id == SetupCatalog.PowerShell ? "shell" : item.Id, inSettings ? 24 : 28);
        var name = _theme.BodyText(item.Name, inSettings ? 13 : 14, 600);
        var status = _theme.BodyText("", 12); status.VerticalAlignment = VerticalAlignment.Center;
        var line = _theme.Caption(item.Line); line.TextWrapping = TextWrapping.Wrap; line.TextTrimming = TextTrimming.None;
        // The command as it will be typed, to read and to select: someone who knows what they want needs nothing else.
        var command = _theme.MonoText(""); command.TextWrapping = TextWrapping.Wrap; command.TextTrimming = TextTrimming.None; command.IsTextSelectionEnabled = true;
        command.Foreground = _theme.Text;
        var commandBox = new Border
        {
            Child = command, Background = _theme.Base, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 6), Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = inSettings ? HorizontalAlignment.Stretch : HorizontalAlignment.Left
        };
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var what = SetupLink("What is this?");
        var page = SetupLink("Install page");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(what, "What is " + item.Name + "?");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(page, item.Name + " install page");
        page.Click += (_, _) => OpenSetupPage(item);
        links.Children.Add(what); links.Children.Add(page);
        var detail = _theme.BodyText(item.Detail, 12); detail.TextWrapping = TextWrapping.Wrap; detail.Foreground = _theme.TextSecondary;
        detail.Visibility = Visibility.Collapsed; detail.Margin = new Thickness(0, 0, 0, 2);
        what.Click += (_, _) => detail.Visibility = detail.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        var addPath = Theme.TextButton("Add to PATH", "Adds " + ClaudeFolder + " to this account's PATH");
        addPath.Click += (_, _) => AddClaudeToPath();
        var start = Theme.TextButton("Start a session");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(start, "Start a " + item.Name + " session");
        start.Click += async (_, _) => await StartSetupSessionAsync(item.Id);
        var install = _theme.PrimaryButton("Install");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(install, "Install " + item.Name);
        install.Click += async (_, _) => await InstallAsync(item);
        var copy = _theme.IconButton(((char)0xE8C8).ToString(), "Copy the command", 32, 14);
        copy.Click += (_, _) =>
        {
            if (SetupCommand(item) is not { } current) return;
            try { ClipboardWrite.SetText(current); UpdateStatus(item.Name + " install command copied"); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { ShowError("The command was not copied: " + exception.Message); }
        };
        // The same order in both places: what was found first, then what installs it.
        actions.Children.Add(start); actions.Children.Add(addPath); actions.Children.Add(install); actions.Children.Add(copy);

        FrameworkElement root;
        if (inSettings)
        {
            var stack = new StackPanel { Spacing = 6, Padding = new Thickness(0, 10, 0, 10) };
            var top = new Grid { ColumnSpacing = 10 };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tile.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(tile);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1); top.Children.Add(name);
            Grid.SetColumn(status, 2); top.Children.Add(status);
            stack.Children.Add(top); stack.Children.Add(line); stack.Children.Add(commandBox); stack.Children.Add(actions); stack.Children.Add(links); stack.Children.Add(detail);
            root = stack;
        }
        else
        {
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 9, 0, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tile.VerticalAlignment = VerticalAlignment.Top; tile.Margin = new Thickness(0, 2, 0, 0);
            grid.Children.Add(tile);
            var text = new StackPanel { Spacing = 3 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            // The two links sit on the name's line: on a line of their own, five rows were taller than the window (a preview, November 27, 2026).
            links.VerticalAlignment = VerticalAlignment.Center; links.Margin = new Thickness(6, 1, 0, 0);
            head.Children.Add(name); head.Children.Add(status); head.Children.Add(links);
            text.Children.Add(head); text.Children.Add(line); text.Children.Add(commandBox); text.Children.Add(detail);
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            Grid.SetColumn(actions, 2); grid.Children.Add(actions);
            root = grid;
        }

        return new SetupRow
        {
            Item = item, InSettings = inSettings, Root = root, Status = status, Line = line, CommandBox = commandBox, Command = command, Detail = detail,
            Install = install, Copy = copy, Page = page, Start = start, AddPath = addPath
        };
    }

    /// <summary>A text link in a row: the accent, no button face.</summary>
    private Button SetupLink(string text)
    {
        var label = new TextBlock { Text = text, FontFamily = Theme.Body, FontSize = 12, Foreground = _theme.Accent };
        return new Button
        {
            Content = label, Padding = new Thickness(0), MinHeight = 0, MinWidth = 0, Background = _theme.Transparent, BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    /// <summary>The rows of <paramref name="inSettings"/>'s place, a hairline between each two that show.</summary>
    private StackPanel BuildSetupRows(bool inSettings)
    {
        var rows = new StackPanel();
        foreach (var item in SetupCatalog.Items)
        {
            var row = BuildSetupRow(item, inSettings);
            row.Divider = _theme.Divider();
            rows.Children.Add(row.Divider);
            rows.Children.Add(row.Root);
            _setupRows.Add(row);
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
        var intro = _theme.BodyText(SetupCatalog.Intro, 13); intro.TextWrapping = TextWrapping.Wrap; intro.Foreground = _theme.TextSecondary;
        intro.Margin = new Thickness(0, 0, 0, 6);
        content.Children.Add(intro);
        content.Children.Add(BuildSetupRows(inSettings: false));
        content.Children.Add(_theme.Divider());
        var footnote = _theme.Tertiary(SetupCatalog.Footnote); footnote.TextWrapping = TextWrapping.Wrap; footnote.TextTrimming = TextTrimming.None;
        footnote.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(footnote);
        // A PC with neither is likely a new one, or its owner new to this: the first row starts open.
        if (SetupGaps == SetupGap.Both && _setupRows.FirstOrDefault(row => !row.InSettings) is { } first) first.Detail.Visibility = Visibility.Visible;
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

    /// <summary>Settings → Agents: the rows of what is still missing, in a section that opens itself while the PC has a gap.</summary>
    private Expander BuildSetupSection()
    {
        var content = new StackPanel();
        content.Children.Add(BuildSetupRows(inSettings: true));
        var footnote = _theme.Tertiary(SetupCatalog.Footnote); footnote.TextWrapping = TextWrapping.Wrap; footnote.TextTrimming = TextTrimming.None;
        footnote.Margin = new Thickness(0, 6, 0, 0);
        content.Children.Add(footnote);
        _setupSection = new Expander
        {
            Header = SetupCatalog.Heading, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = SetupGaps != SetupGap.None, Visibility = Visibility.Collapsed
        };
        RefreshSetupRows();
        return _setupSection;
    }

    /// <summary>Both places in step with the PC, after each detection: the notice, the panel and every row.</summary>
    internal void RefreshSetupAdvice()
    {
        RefreshSetupRows();
        if (SetupNoticeShown && (SetupHomeShown || SetupAdvisor.Open(SetupGaps, _preferences.SetupClosedAtLaunch) != _setupNoticeGaps)) ShowSetupNotice();
    }

    /// <summary>
    /// Shows or hides the home panel and brings every row up to date: found or not, the command this PC can run, and the
    /// buttons that go with them. The home panel lists everything; Settings lists what is missing, and Claude Code while its
    /// folder is off Path.
    /// </summary>
    internal void RefreshSetupRows()
    {
        var gaps = SetupGaps;
        var home = _setupReady && _setupHomeSlot is not null && gaps != SetupGap.None && (_setupReopened || SetupAdvisor.Open(gaps, _preferences.SetupClosedOnHome) != SetupGap.None);
        if (home && _setupPanel is null && !_closing) { _setupPanel = BuildSetupPanel(); _setupHomeSlot!.Child = _setupPanel; }
        if (_setupPanel is { } panel)
        {
            panel.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            // The mark and the tagline give their room to the panel, and the column widens for the commands.
            if (_homeMark is not null) _homeMark.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (_homeTagline is not null) _homeTagline.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (_homeStack is not null) _homeStack.MaxWidth = home ? 720 : 480;
        }
        var checking = _detection.Count == 0 && !_detectionFailed && SetupGapsOverride is null;
        var offPath = ClaudeOffPath();
        var (winget, npm) = (SetupWinget, SetupNpm);
        foreach (var place in _setupRows.GroupBy(row => row.InSettings))
        {
            var first = true;
            foreach (var row in place)
            {
                var agent = row.Item.Id != SetupCatalog.PowerShell;
                var found = SetupFound(row.Item, gaps);
                var needsPath = found && row.Item.Id == "claude" && offPath;
                var command = found ? null : SetupCatalog.Command(row.Item.Id, winget, npm);
                (row.Status.Text, row.Status.Foreground) = found ? ("Detected", _theme.Success) : agent && checking ? ("Checking…", _theme.TextTertiary) : ("Not found", _theme.TextTertiary);
                row.Line.Text = needsPath ? SetupCatalog.ClaudeOffPath : row.Item.Line;
                row.Command.Text = command ?? "";
                row.CommandBox.Visibility = command is null ? Visibility.Collapsed : Visibility.Visible;
                // Where this PC cannot run a command (no winget, no npm), the button opens the vendor's page instead, and the link beside
                // "What is this?" would only repeat it.
                row.Install.Content = command is null ? "Install page" : "Install";
                row.Install.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
                row.Copy.Visibility = command is null ? Visibility.Collapsed : Visibility.Visible;
                row.Page.Visibility = command is null ? Visibility.Collapsed : Visibility.Visible;
                row.Start.Visibility = found && agent ? Visibility.Visible : Visibility.Collapsed;
                row.AddPath.Visibility = needsPath ? Visibility.Visible : Visibility.Collapsed;
                var shown = !row.InSettings || !found || needsPath;
                row.Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                if (row.Divider is { } divider) divider.Visibility = shown && !first ? Visibility.Visible : Visibility.Collapsed;
                if (shown) first = false;
            }
        }
        if (_setupSection is { } section)
        {
            section.Visibility = _setupRows.Any(row => row.InSettings && row.Root.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            // Detection answers after the section is built, so a PC without an agent opens it here, once.
            if (gaps != SetupGap.None && !_setupSectionOpened) { _setupSectionOpened = true; section.IsExpanded = true; }
        }
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
    /// Install: a PowerShell tab named for the item, in the profile folder, with the command typed at its prompt once the
    /// prompt is there, and not run. Text sent while the pane was still starting was lost in a Windows Sandbox run, so the
    /// typing waits for the prompt (<see cref="ShellPrompt"/>), or for the screen to stop changing under a profile's own prompt.
    /// </summary>
    internal async Task InstallAsync(SetupItem item)
    {
        if (SetupCommand(item) is not { } command) { OpenSetupPage(item); return; }
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
    /// the installers with no known place (Hermes, npm), which are also found when they change Path. Found: the notice waits for
    /// the tab's screen to stop changing, since claude.exe is in place a moment before its installer has finished talking.
    /// The watch ends when the item is found and said, or its tab has gone.
    /// </summary>
    private async Task InstallTickAsync()
    {
        if (_installTicking || _closing) return;
        _installTicking = true;
        try
        {
            var detect = false;
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
                    else if (install.Item.Id != SetupCatalog.PowerShell && install.Ticks % 8 == 0 && DateTimeOffset.UtcNow - install.Started < InstallWatch) detect = true;
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
                ShowInstalled(install.Item);
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

    /// <summary>The notice once an install is found: what is installed, Start a session for an agent, and Add to PATH for Claude Code off Path.</summary>
    internal void ShowInstalled(SetupItem item)
    {
        if (_closing) return;
        RefreshSetupRows();
        var agent = item.Id != SetupCatalog.PowerShell;
        var offPath = item.Id == "claude" && ClaudeOffPath();
        var message = !agent ? "PowerShell 7 is installed. New tabs run in it." : offPath ? SetupCatalog.ClaudeOffPathNotice : item.Name + " is installed.";
        Diagnostics.Log("setup: " + item.Id + " installed" + (offPath ? ", its folder off Path" : ""));
        ShowNotice(InfoBarSeverity.Success, "", message, null, brief: false, content: agent ? InstalledActions(item.Id, offPath) : null);
    }

    private StackPanel InstalledActions(string id, bool offPath)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        if (offPath)
        {
            var add = Theme.TextButton("Add to PATH", "Adds " + ClaudeFolder + " to this account's PATH");
            add.Click += (_, _) => AddClaudeToPath();
            actions.Children.Add(add);
        }
        var start = Theme.TextButton("Start a session");
        start.Click += async (_, _) => await StartSetupSessionAsync(id);
        actions.Children.Add(start);
        return actions;
    }

    /// <summary>The key Add to PATH writes under, in place of the account's own, for the smoke; null in use.</summary>
    internal string? UserPathKeyOverride { get; set; }

    /// <summary>
    /// Add to PATH: Claude Code's folder goes on the end of the account's Path (<see cref="UserPath"/>), and the notice says
    /// so, with Start a session still there. Tabs already open keep the Path they started with; new ones have it.
    /// </summary>
    internal void AddClaudeToPath()
    {
        try
        {
            var added = UserPath.Add(ClaudeFolder, UserPathKeyOverride ?? UserPath.EnvironmentKey);
            Diagnostics.Log("setup: " + ClaudeFolder + (added ? " added to the account's Path" : " was already on the account's Path"));
            ShowNotice(InfoBarSeverity.Success, "", SetupCatalog.ClaudeOnPathNotice, null, brief: false, content: InstalledActions("claude", offPath: false));
            RefreshSetupRows();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ShowError("The account's PATH was not changed: " + exception.Message);
        }
    }

    /// <summary>Start a session, from a row or the installed notice: New session with that agent picked, switched on if it was off.</summary>
    private async Task StartSetupSessionAsync(string id)
    {
        if (!AgentOn(id)) SetAgentEnabled(id, true);
        if (_notice.IsOpen && LastNotice is { Severity: InfoBarSeverity.Success }) _notice.IsOpen = false;
        await ShowNewSessionAsync(agent: id);
    }
}
