using Sic.Core.Agents;
using Sic.Core.Sessions;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Sic.App;

internal sealed partial class MainWindow
{
    private ContentDialog Dialog(string title, object content, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary,
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = _root.RequestedTheme
        };
        _theme.Accentuate(dialog.Resources);
        // The drag areas sit above everything in the window, the dialog and its overlay included, so they stand down while
        // a dialog is up, as WinUI's own title bar does.
        dialog.Opened += (_, _) => { _dialogsOpen++; UpdateDragRegions(); };
        dialog.Closed += (_, _) => { _dialogsOpen = Math.Max(0, _dialogsOpen - 1); UpdateDragRegions(); };
        return dialog;
    }

    /// <summary>The dialog F1 opened, while it is up; for verification.</summary>
    private ContentDialog? _keyboardHelp;
    private ScrollViewer? _keyboardHelpScroll;

    /// <summary>
    /// F1: the keyboard reference (<see cref="KeyboardReference"/>) in a dialog of its own, under the mark and the name as
    /// Settings → About shows them. Close or Esc closes it. Nothing opens while another dialog is up, since WinUI shows one at a time.
    /// </summary>
    private async Task ShowKeyboardHelpAsync()
    {
        if (_dialogsOpen > 0 || _keyboardHelp is not null) return;
        // A grid whose last row takes what height is left: the list scrolls in it when the window is too short for all of it.
        // ContentDialog's own scroll viewer does not scroll down, and in a StackPanel the list ran past the dialog's bottom, cut off
        // (Chris, November 12, 2026).
        var content = new Grid { RowSpacing = 14 };
        for (var row = 0; row < 4; row++) content.RowDefinitions.Add(new RowDefinition { Height = row == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        void Row(FrameworkElement element) { Grid.SetRow(element, content.Children.Count); content.Children.Add(element); }
        Row(BuildAbout(tagline: false));
        Row(_theme.Divider());
        Row(_theme.Eyebrow("Keyboard shortcuts"));
        var list = BuildShortcutList();
        // Room beside the list for the scroll bar, which would lie over the text.
        list.Margin = new Thickness(0, 0, 16, 0);
        Row(_keyboardHelpScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var dialog = Dialog("", content, "");
        dialog.Title = null;
        dialog.CloseButtonText = "Close";
        dialog.DefaultButton = ContentDialogButton.Close;
        // Room for the keys and what they do side by side; ContentDialog's own limit is 548.
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, "Keyboard shortcuts");
        _keyboardHelp = dialog;
        dialog.Closed += (_, _) => _keyboardHelp = null;
        // Refused when another dialog was asked for in the same moment and has not opened yet.
        try { await dialog.ShowAsync(); }
        catch (Exception) { _keyboardHelp = null; }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary) =>
        await Dialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, FontFamily = Theme.Body }, primary).ShowAsync() == ContentDialogResult.Primary;

    /// <summary>The multi-line paste question while it is up: the dialog, the text it shows and its Don't ask again box; for verification.</summary>
    private sealed record PasteQuestionView(ContentDialog Dialog, TextBlock Text, CheckBox DontAsk);
    private PasteQuestionView? _pasteQuestion;

    /// <summary>
    /// Settings → Terminal → Confirm multi-line pastes into PowerShell (docs/decisions/clipboard.md): the text as it will be
    /// sent, the first 4,000 characters of it, with Paste, the default as in Windows Terminal, and Cancel. Don't ask again, with
    /// Paste, turns the setting off. The keyboard starts on the text rather than on the box, so a stray Space does not tick it.
    /// With another dialog up the paste is not sent, since WinUI shows one at a time and the paste cannot have been meant then.
    /// </summary>
    private async Task<bool> ConfirmPasteAsync(string text)
    {
        if (_dialogsOpen > 0 || _pasteQuestion is not null) return false;
        var shown = text.Replace('\r', '\n');
        if (shown.Length > 4000) shown = shown[..(char.IsHighSurrogate(shown[3999]) ? 3999 : 4000)] + "\n…";
        var content = new StackPanel { Spacing = 12, MinWidth = 400, MaxWidth = 520 };
        var intro = _theme.BodyText("PowerShell runs each line the moment it is pasted.", 13);
        intro.TextWrapping = TextWrapping.Wrap; intro.TextTrimming = TextTrimming.None;
        content.Children.Add(intro);
        var preview = new TextBlock { Text = shown, FontFamily = Theme.Mono, FontSize = 12, Foreground = _theme.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        content.Children.Add(new Border
        {
            Background = _theme.Layer, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8),
            Child = new ScrollViewer { Content = preview, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });
        var dontAsk = new CheckBox { Content = "Don't ask again" };
        content.Children.Add(dontAsk);
        var title = "Paste " + ClipboardText.LineCount(text) + " lines into PowerShell?";
        var focusHost = new ContentControl
        {
            Content = content, IsTabStop = true, UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(focusHost, title);
        var dialog = Dialog(title, focusHost, "Paste");
        _pasteQuestion = new PasteQuestionView(dialog, preview, dontAsk);
        dialog.Closed += (_, _) => _pasteQuestion = null;
        ContentDialogResult result;
        // Refused when another dialog was asked for in the same moment and has not opened yet.
        try { result = await dialog.ShowAsync(); }
        catch (Exception) { _pasteQuestion = null; return false; }
        if (result != ContentDialogResult.Primary) return false;
        if (dontAsk.IsChecked == true) SetConfirmMultiLinePaste(false);
        return true;
    }

    private enum CloseChoice { Graceful, Force, Cancel }

    /// <summary>Exit cleanly (default), Force, or Cancel, for closing anything with a live agent.</summary>
    private async Task<CloseChoice> AskCloseAsync(string title, string message)
    {
        var dialog = Dialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, FontFamily = Theme.Body }, "Exit cleanly");
        dialog.SecondaryButtonText = "Force";
        ContentDialogResult result;
        // Refused when another dialog was shown but has not opened yet; the default answer stands, as when one is already open.
        try { result = await dialog.ShowAsync(); }
        catch (Exception) { return CloseChoice.Graceful; }
        return result switch
        {
            ContentDialogResult.Primary => CloseChoice.Graceful,
            ContentDialogResult.Secondary => CloseChoice.Force,
            _ => CloseChoice.Cancel
        };
    }

    /// <summary>The quit dialog while it is open: its session rows and summary line, for verification.</summary>
    private sealed record ExitProgressView(ContentDialog Dialog, ContentControl FocusHost, StackPanel Rows, TextBlock Summary);
    private ExitProgressView? _exitProgress;

    /// <summary>
    /// Quitting cleanly: a dialog lists every live session, each is asked to exit, and the dialog closes itself once all have
    /// exited or run out of time, leaving the result on screen for a moment. Force quit now stops the waiting, and the
    /// teardown that follows stops whatever is still running. The exit sequence starts as the dialog opens (an unsent draft is
    /// discarded at once) and cannot be recalled, so there is no Cancel, and Esc does nothing. The keyboard focus starts on
    /// the content, not on Force quit now, so an Enter or Space still held from the Quit that opened it presses nothing.
    /// Returns how many were still running.
    /// </summary>
    private async Task<int> ExitWithProgressAsync(IReadOnlyList<SessionPane> panes)
    {
        var running = panes.Where(pane => pane.IsRunning).ToArray();
        if (running.Length == 0) return 0;
        // WinUI shows one dialog at a time; with another already up, the status bar reports instead.
        if (_dialogsOpen > 0) return await ExitPanesAsync(running);

        var content = new StackPanel { Spacing = 12, MinWidth = 400, MaxWidth = 460 };
        var intro = _theme.Caption($"Each agent is asked to end its own session so its history and locks are saved. SicTerm closes once they have exited; anything still running after {ExitBudget.TotalSeconds:0} seconds is stopped.");
        intro.TextWrapping = TextWrapping.Wrap; intro.TextTrimming = TextTrimming.None;
        content.Children.Add(intro);
        var rows = new StackPanel { Spacing = 8 };
        var states = new Dictionary<SessionPane, TextBlock>();
        foreach (var pane in running)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tile = _theme.AgentTile(pane.Descriptor.AgentId, 16); tile.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(tile);
            var label = _theme.BodyText(ExitLabel(pane), 13); label.VerticalAlignment = VerticalAlignment.Center; label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(label, pane.Descriptor.WorkingDirectory);
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var state = _theme.Tertiary(ExitProgress.State(null)); state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 2); row.Children.Add(state);
            states[pane] = state;
            rows.Children.Add(row);
        }
        content.Children.Add(rows);
        var bar = new ProgressBar { IsIndeterminate = true, Maximum = running.Length };
        content.Children.Add(bar);
        var summary = _theme.BodyText(ExitProgress.Summary(running.Length, 0, 0), 13);
        summary.TextWrapping = TextWrapping.Wrap; summary.TextTrimming = TextTrimming.None;
        content.Children.Add(summary);

        // ContentDialog gives the first focusable element of its content the focus before its buttons; without one here the
        // only visible button, Force quit now, would take it.
        var focusHost = new ContentControl
        {
            Content = content, IsTabStop = true, UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(focusHost, "Closing sessions cleanly");
        var dialog = Dialog("Closing sessions cleanly", focusHost, "");
        dialog.CloseButtonText = "";
        dialog.SecondaryButtonText = "Force quit now";
        dialog.DefaultButton = ContentDialogButton.None;
        var done = false;
        dialog.Closing += (_, args) => { if (!done && args.Result != ContentDialogResult.Secondary) args.Cancel = true; };
        dialog.Closed += (_, _) => _exitProgress = null;
        _exitProgress = new ExitProgressView(dialog, focusHost, rows, summary);

        Task<ContentDialogResult> showing;
        try { showing = dialog.ShowAsync().AsTask(); }
        catch (Exception) { _exitProgress = null; return await ExitPanesAsync(running); }
        int exited = 0, stubborn = 0;
        var exits = ExitPanesAsync(running, (pane, ok) =>
        {
            if (ok) exited++; else stubborn++;
            states[pane].Text = ExitProgress.State(ok);
            states[pane].Foreground = ok ? _theme.Success : _theme.Warning;
            summary.Text = ExitProgress.Summary(running.Length, exited, stubborn);
            if (exited + stubborn == running.Length) { bar.IsIndeterminate = false; bar.Value = bar.Maximum; }
        });
        if (await Task.WhenAny(exits, showing) == exits)
        {
            await Task.WhenAny(Task.Delay(700), showing);
            done = true;
            dialog.Hide();
        }
        // A dialog that could not be shown leaves the status bar to report, and the exits run their course.
        try { await showing; }
        catch (Exception) { _exitProgress = null; await exits; }
        // Forced: whatever has not exited by now is stopped by the teardown that follows.
        return running.Length - exited;
    }

    /// <summary>A session's name in the quit dialog: its tab's name, or for a pane of a split tab, the pane's own name in that tab.</summary>
    private string ExitLabel(SessionPane pane) => HostOf(pane) is { Panes.Count: 1 } host
        ? host.Title.Text
        : pane.Label + (HostOf(pane) is { } split ? " · in " + DisplayTitle(split.Saved) : "");
    /// <summary>The New session dialog, its folder box and its agent tiles while the dialog is up; for verification.</summary>
    private ContentDialog? _newSessionDialog;
    private TextBox? _newSessionFolder;
    private StackPanel? _newSessionTiles;

    /// <summary>
    /// Agent tiles, a working folder with Browse and recent picks, then Start. With a split target the session opens beside or
    /// below that pane. The panel stays as it is: with tabs spread over workspaces it is how the rest are found, so a New session
    /// here no longer closes it (Chris, November 9, 2026).
    /// </summary>
    private async Task ShowNewSessionAsync(string? folder = null, SplitTarget? target = null, string? agent = null)
    {
        try
        {
            await ShowNewSessionDialogAsync(folder, target, agent);
        }
        catch (Exception exception) { ShowError(exception.Message); }
        finally { _newSessionDialog = null; _newSessionFolder = null; _newSessionTiles = null; }
    }

    private async Task ShowNewSessionDialogAsync(string? folder, SplitTarget? target, string? agent)
    {
        var content = new StackPanel { Spacing = 14, MinWidth = 460 };
        content.Children.Add(_theme.Eyebrow("Agent"));
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var group = new List<ToggleButton>();
        // A tile added while the dialog is up (found) goes before PowerShell's and leaves the choice already made alone.
        void AddChoice(string id, string name, bool detected, bool found = false)
        {
            var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(_theme.AgentTile(id, 28));
            var label = _theme.BodyText(name, 12); label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            if (!detected) { var missing = _theme.Tertiary("not detected"); missing.FontSize = 10; missing.Foreground = _theme.Warning; missing.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(missing); }
            var button = new ToggleButton { Content = stack, Tag = id, Width = 104, Height = 84, Padding = new Thickness(4), IsChecked = !found && id == (agent ?? _preferences.PreferredAgent) };
            // The tile had no name of its own for a screen reader or UI Automation, only its picture and text inside.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, detected ? name : name + ", not detected");
            foreach (var key in new[] { "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed" }) Theme.Override(button.Resources, key, _theme.AccentSoft);
            foreach (var key in new[] { "ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed" }) Theme.Override(button.Resources, key, _theme.Accent);
            foreach (var key in new[] { "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" }) Theme.Override(button.Resources, key, _theme.Text);
            button.Checked += (sender, _) => { foreach (var other in group) if (!ReferenceEquals(other, sender)) other.IsChecked = false; };
            button.Unchecked += (sender, _) => { if (group.All(item => item.IsChecked != true)) ((ToggleButton)sender).IsChecked = true; };
            if (found) { group.Insert(group.Count - 1, button); tiles.Children.Insert(tiles.Children.Count - 1, button); }
            else { group.Add(button); tiles.Children.Add(button); }
        }
        foreach (var definition in _preferences.Agents.Where(agent => agent.IsOn)) AddChoice(definition.Id, definition.Name, _available.GetValueOrDefault(definition.Id));
        AddChoice("shell", "PowerShell", true);
        if (group.All(item => item.IsChecked != true)) group[0].IsChecked = true;
        content.Children.Add(tiles);
        // No agent on this PC: the dialog says so and leads to Get set up (MainWindow.Setup.cs). It had shown PowerShell alone under
        // "Agent", with nothing to say agents exist (a Windows Sandbox run, November 27, 2026).
        var setUp = false;
        if (SetupGaps.HasFlag(Sic.Core.Shell.SetupGap.Agent))
        {
            var none = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            none.Children.Add(_theme.Caption(Sic.Core.Shell.SetupAdvisor.For(Sic.Core.Shell.SetupGap.Agent)!.Message));
            var link = new HyperlinkButton { Content = "Set up", Padding = new Thickness(0), MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
            link.Click += (_, _) => { setUp = true; _newSessionDialog?.Hide(); };
            none.Children.Add(link);
            content.Children.Add(none);
        }

        content.Children.Add(_theme.Eyebrow("Working folder"));
        var folderRow = new Grid { ColumnSpacing = 8 };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var directory = new TextBox
        {
            // The folder named, else the one chosen in a folder tree, else the focused pane's, the current workspace's folder, the first listed folder, the profile (NewSessionFolder).
            Text = folder ?? _selectedFolder ?? NewSessionFolder,
            FontFamily = Theme.Mono, MinWidth = 300
        };
        folderRow.Children.Add(directory);
        var recentFolders = RecentFolders();
        var recent = new DropDownButton { Content = Theme.Icon("", 14), MinHeight = 32 };
        ToolTipService.SetToolTip(recent, "Recent folders");
        var recentMenu = new MenuFlyout();
        foreach (var path in recentFolders)
        {
            var item = new MenuFlyoutItem { Text = Paths.Shorten(path, 3) };
            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => directory.Text = path;
            recentMenu.Items.Add(item);
        }
        recent.Flyout = recentMenu; recent.IsEnabled = recentFolders.Length > 0;
        Grid.SetColumn(recent, 1); folderRow.Children.Add(recent);
        var browse = Theme.TextButton("Browse…");
        browse.Click += async (_, _) =>
        {
            // An exception in this async void handler would end SIC through the unhandled-exception handler; say so instead.
            // The picker opens in the folder the box names, so a subfolder of the workspace's folder is a click away.
            try { if (await PickFolderAsync(directory.Text) is { } selected) directory.Text = selected; }
            catch (Exception exception) { ShowError("The folder picker did not open: " + exception.Message); }
        };
        Grid.SetColumn(browse, 2); folderRow.Children.Add(browse);
        content.Children.Add(folderRow);
        var hint = _theme.Caption(target is null
            ? "The agent runs in this folder with its own authentication and approval prompts intact."
            : (target.Direction == PaneSplit.Columns ? "The new session opens to the right of pane “" : "The new session opens below pane “") + target.Pane.Label
                + "”, in the same tab, taking half of its room." + (target.Host.Panes.Count + 1 == PaneLayout.MaxPanes ? " That fills the tab." : ""));
        hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None;
        content.Children.Add(hint);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = _theme.Danger, FontFamily = Theme.Body };
        content.Children.Add(error);
        var dialog = Dialog(target is null ? "New session" : "Split: new session", content, "Start");
        // Room for a row of six agent tiles; ContentDialog's own limit is 548.
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        dialog.Opened += (_, _) => { directory.Focus(FocusState.Programmatic); directory.SelectAll(); };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!Path.IsPathFullyQualified(directory.Text) || !Directory.Exists(directory.Text))
            { args.Cancel = true; error.Text = "Choose an existing absolute folder path."; }
        };
        _newSessionDialog = dialog; _newSessionFolder = directory; _newSessionTiles = tiles;
        // An agent installed since detection last looked: the detection under way, or one started now (DetectForNewSession),
        // turns it on, and its tile joins the dialog if it is still up.
        var looking = (Task?)_detect.Current ?? (DetectForNewSession ? DetectToolsAsync() : null);
        if (looking is not null) _ = AddFoundTilesAsync(looking);
        async Task AddFoundTilesAsync(Task detection)
        {
            await detection;
            if (!ReferenceEquals(_newSessionDialog, dialog)) return;
            foreach (var definition in _preferences.Agents.Where(agent => agent.IsOn && group.All(item => (string)item.Tag != agent.Id)))
                AddChoice(definition.Id, definition.Name, _available.GetValueOrDefault(definition.Id), found: true);
        }
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { if (setUp) OpenSetup(); return; }
        // A folder outside every listed one may join the list first, or the session goes to Scratch (OfferFolderAsync); a split stays in its tab.
        if (target is null && !await OfferFolderAsync(directory.Text)) return;
        var id = (string)group.First(item => item.IsChecked == true).Tag;
        if (id != "shell") { _preferences.PreferredAgent = id; SavePreferences(); }
        await StartNewAsync(id, directory.Text, target);
    }

    /// <summary>
    /// The New session dialog's Recent menu: the open panes' folders, the listed folders and the listed conversations' folders,
    /// ten at most. The list's group labels sit among its rows and hold no conversation, so rows are taken by what they hold
    /// rather than cast; casting them is what made the + button fail once the list was grouped.
    /// </summary>
    private string[] RecentFolders() => AllPanes.Select(pane => pane.Descriptor.WorkingDirectory)
        .Concat(Folders)
        .Concat(_history.Items.OfType<ListViewItem>().Select(item => item.Tag).OfType<Conversation>().Select(conversation => conversation.WorkingDirectory ?? ""))
        .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();

    private async Task LinkPaneAsync(SessionPane pane)
    {
        try
        {
            await RefreshHistoryAsync();
            var search = new TextBox { PlaceholderText = "Filter by title or transcript text" };
            var choices = new ComboBox { PlaceholderText = "Choose the exact saved conversation", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 420 };
            var directory = new TextBox { Header = "Working folder for future resume", Text = pane.Descriptor.WorkingDirectory, FontFamily = Theme.Mono };
            var error = new TextBlock { Foreground = _theme.Danger, TextWrapping = TextWrapping.Wrap };
            // The choices are the newest matches, found off the UI thread once the typing pauses, as the Sessions list finds its
            // own; a keystroke during the search starts over. Fifty at most: a longer list is a filter to refine, not a menu to scroll.
            const int mostChoices = 50;
            CancellationTokenSource? filtering = null;
            async Task PopulateAsync(bool debounce)
            {
                filtering?.Cancel(); filtering?.Dispose();
                filtering = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                var cancellation = filtering.Token;
                var query = search.Text; var agent = pane.Descriptor.AgentId;
                var names = new Dictionary<string, string>(_preferences.ConversationNames, StringComparer.Ordinal);
                try
                {
                    if (debounce) await Task.Delay(150, cancellation);
                    var found = await Task.Run(() => _catalog.Search(query, agent, mostChoices, aliases: names), cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    choices.Items.Clear();
                    foreach (var conversation in found) choices.Items.Add(new ComboBoxItem { Content = conversation.Title + "\n" + conversation.SessionId, Tag = conversation });
                    if (found.Count == mostChoices) choices.Items.Add(new ComboBoxItem { Content = "Newest " + mostChoices + " shown · refine the filter", IsEnabled = false });
                    choices.SelectedIndex = -1;
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { error.Text = exception.Message; }
            }
            _ = PopulateAsync(debounce: false);
            search.TextChanged += (_, _) => _ = PopulateAsync(debounce: true);
            choices.SelectionChanged += (_, _) =>
            {
                if (choices.SelectedItem is ComboBoxItem { Tag: Conversation conversation } && !string.IsNullOrEmpty(conversation.WorkingDirectory)) directory.Text = conversation.WorkingDirectory;
            };
            var content = new StackPanel { Spacing = 12 };
            var intro = _theme.Caption("Choose the session ID belonging to this pane. Linking changes only SicTerm's saved tab metadata; it does not restart or take over an agent. A new Codex pane links itself once Codex saves its first message; this is for a pane that was opened before that or whose session was started elsewhere.");
            intro.TextWrapping = TextWrapping.Wrap; intro.TextTrimming = TextTrimming.None; intro.MaxWidth = 440;
            content.Children.Add(intro);
            content.Children.Add(search); content.Children.Add(choices); content.Children.Add(directory); content.Children.Add(error);
            var dialog = Dialog("Link saved conversation", content, "Link this session");
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (choices.SelectedItem is not ComboBoxItem || !Path.IsPathFullyQualified(directory.Text) || !Directory.Exists(directory.Text))
                { args.Cancel = true; error.Text = "Select a conversation and an existing absolute working folder."; }
            };
            var linked = await dialog.ShowAsync() == ContentDialogResult.Primary;
            filtering?.Cancel();
            if (!linked) return;
            var selected = (Conversation)((ComboBoxItem)choices.SelectedItem).Tag;
            var host = HostOf(pane) ?? throw new InvalidOperationException("The pane is no longer open.");
            pane.UpdateIdentity(pane.Descriptor with { SessionId = selected.SessionId, Title = selected.Title, WorkingDirectory = directory.Text, HistoryDirectory = selected.HistoryDirectory, CreationName = null, Unsaved = false });
            // A name given to the pane, or to a tab of that one pane, now belongs to the conversation; a local name the
            // conversation already has shows on the pane, and on the tab when the tab has no name of its own.
            AdoptIdentity(host, pane, selected.Key);
            host.Saved = host.Saved with { Panes = host.Panes.Select(item => item.Descriptor).ToList() };
            if (host.Panes.Count == 1) host.Saved = host.Saved with { Title = _catalog.GetHeader(selected.Key)?.Title ?? selected.Title };
            host.Item.Tag = host.Saved;
            if (pane.Terminal is null) ShowSavedPreview(pane, selected.Key);
            RefreshTabStatus(host);
            PaintTab(host);
            SaveLayout(); UpdateStatus("Exact saved session linked"); RefreshConversationRows();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }
}
