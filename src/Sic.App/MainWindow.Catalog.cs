using System.Text.RegularExpressions;
using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.Appearance;
using Sic.Core.Sessions;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Sic.App;

/// <summary>
/// The Sessions panel (search, agent filter, folder scope, the conversation list, the in-place preview) and the folder trees the
/// Workspaces panel shows under each workspace's home folder (MainWindow.Workspaces.cs), with the folder rows' emoji and menus.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly AutoSuggestBox _search = new() { PlaceholderText = "Search conversations", QueryIcon = new SymbolIcon(Symbol.Find) };
    /// <summary>The view menu's agent items, keyed by agent id, "" for every agent; an item of an agent that is off is hidden (RefreshAgentFilterBar).</summary>
    private readonly Dictionary<string, RadioMenuFlyoutItem> _agentItems = new(StringComparer.Ordinal);
    /// <summary>The view menu's grouping and sort items, keyed by their <see cref="ConversationListing"/> words (docs/decisions/sessions-groups.md).</summary>
    private readonly Dictionary<string, RadioMenuFlyoutItem> _groupingItems = new(StringComparer.Ordinal), _sortItems = new(StringComparer.Ordinal);
    /// <summary>The view menu: grouping, sort, the agents, the folder scope, and Fold all groups.</summary>
    private readonly MenuFlyout _filterMenu = new();
    private Button _filterButton = null!;
    private MenuFlyoutItem _foldAll = null!;
    /// <summary>Each section the list shows: its header row and the rows under it, in the list's order; emptied with the list. A fold hides the rows (ApplyFolds).</summary>
    private readonly List<(ConversationSection Section, ListViewItem Row, SectionHeader Header, List<ListViewItem> Rows)> _sections = [];
    /// <summary>Refresh history, beside the filter; disabled while a refresh runs, its tooltip saying when the last one was.</summary>
    private Button _refreshButton = null!;
    /// <summary>Refreshes that have finished, for verification.</summary>
    internal int HistoryRefreshes { get; private set; }
    /// <summary>The sessions left out of a refresh for their id ("agent:id") already logged and noticed this run (NoteUnlistedSessions).</summary>
    private readonly HashSet<string> _unlistedSessions = new(StringComparer.Ordinal);
    private ToggleMenuFlyoutItem? _scopeItem;
    private readonly ListView _history = new() { SelectionMode = ListViewSelectionMode.Single, Footer = new Border { Height = PanelFootBuffer } };
    /// <summary>The folder each node of every workspace's folder tree stands for.</summary>
    private readonly Dictionary<TreeViewNode, string> _folderPaths = [];
    /// <summary>The line under the search: the filters in force and the count, with a button that clears the filters while any is set.</summary>
    private TextBlock _historyCount = null!;
    private Button _clearFilters = null!;
    private string _countText = "";
    /// <summary>The selected conversation's detail (excerpt, note, Resume, Open menu), shown inside its row: one element, moved between rows (PlacePreview).</summary>
    private Border _previewCard = null!;
    private TextBlock _previewExcerpt = null!;
    private Border _previewState = null!;
    private TextBlock _previewStateText = null!;
    private Microsoft.UI.Xaml.Shapes.Ellipse _previewStateDot = null!, _groupStateDot = null!;
    private TextBlock _previewNote = null!;
    private Button _previewResume = null!;
    private Conversation? _selectedConversation;
    /// <summary>The pinned tab whose detail is open in its row, or null (docs/decisions/tab-groups.md).</summary>
    private SavedTab? _selectedGroup;
    private Border _groupDetail = null!;
    private StackPanel _groupPanes = null!;
    private Button _groupResume = null!;
    private Border _groupState = null!;
    private TextBlock _groupStateText = null!;
    /// <summary>Each listed pinned tab and the slot its open-in-a-tab dot goes in, in the list's order; emptied with the list.</summary>
    private readonly List<(SavedTab Group, Border Slot)> _groupMarks = [];
    private CancellationTokenSource? _previewCancellation;
    /// <summary>The folder last chosen in a workspace's folder tree: where the New session dialog starts when nothing nearer says.</summary>
    private string? _selectedFolder;
    /// <summary>
    /// The folder Ctrl+Shift+E opens in File Explorer rather than the focused pane's: the one last clicked in a panel, a folder of
    /// the Folders panel or a workspace's row in Agent Workspaces, until a pane is clicked or takes the focus or another
    /// workspace is switched to (Chris, November 12, 2026).
    /// </summary>
    private string? _explorerFolder;
    /// <summary>The group labels currently in the list, registered for the display font; replaced on every rebuild.</summary>
    private readonly List<TextBlock> _groupLabels = [];
    private const string PinGlyph = Theme.PinGlyph;
    /// <summary>An open folder, as on the pane header's button that opens its working folder in File Explorer.</summary>
    private const string ExplorerGlyph = "\uE838";
    /// <summary>Each folder row's repaint, so an emoji given to a folder shows on every row of it, in whichever workspace's tree.</summary>
    private readonly Dictionary<TreeViewNode, Action> _folderMarks = [];
    /// <summary>
    /// The unseen box Windows' emoji panel types into (docs/decisions/folder-emoji.md), in the Workspaces panel: Emoji… on a
    /// workspace's menu focuses it and opens the panel, and the emoji chosen is the home folder's at once (<see cref="_emojiFolder"/>).
    /// </summary>
    private TextBox _folderEmojiBox = null!;
    /// <summary>The folder the next emoji typed into the box is for: the home of the workspace whose menu asked.</summary>
    private string? _emojiFolder;
    private bool _syncingFolderEmoji;

    private UIElement BuildSessionsPanel()
    {
        var grid = new Grid { RowSpacing = 10, Padding = new Thickness(12, 0, 12, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // The search row: the box, a view button whose menu holds the grouping, the sort, the agents, the folder scope and Fold
        // all groups (docs/decisions/sessions-groups.md), and a Refresh button. A bar of the agents sat under the box until
        // October 8, 2026; six agents no longer fit it at the panel's width.
        var searchRow = new Grid { ColumnSpacing = 6 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _theme.Accentuate(_search.Resources);
        _search.TextChanged += async (_, args) => { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) await SearchAsync(debounce: true); };
        _search.QuerySubmitted += async (_, _) => await SearchAsync();
        searchRow.Children.Add(_search);
        _filterButton = _theme.IconButton("\uE8CB", "Group and sort the list, or show one agent or folder", 32, 14);
        AutomationProperties.SetName(_filterButton, "View");
        foreach (var (word, text) in new[] { (ConversationListing.ByFolder, "By workspace"), (ConversationListing.ByDate, "By date"), (ConversationListing.ByAgent, "By agent") })
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "grouping" };
            item.Click += async (_, _) => await SetGroupingAsync(word);
            _filterMenu.Items.Add(item); _groupingItems[word] = item;
        }
        _filterMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var (word, text) in new[] { (ConversationListing.Newest, "Newest first"), (ConversationListing.ByName, "By name") })
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "sort" };
            item.Click += async (_, _) => await SetSortAsync(word);
            _filterMenu.Items.Add(item); _sortItems[word] = item;
        }
        _filterMenu.Items.Add(new MenuFlyoutSeparator());
        var all = new RadioMenuFlyoutItem { Text = "All agents", GroupName = "agent" };
        all.Click += async (_, _) => await SetAgentFilterAsync(null);
        _filterMenu.Items.Add(all); _agentItems[""] = all;
        foreach (var agent in _preferences.Agents)
        {
            var item = new RadioMenuFlyoutItem { Text = agent.Name, GroupName = "agent" };
            if (_theme.AgentImage(agent.Id, 16) is BitmapImage image) item.Icon = new ImageIcon { Source = image };
            // An agent switched off keeps its item, hidden, so switching it on brings it back in its place (RefreshAgentFilterBar).
            item.Visibility = agent.IsOn ? Visibility.Visible : Visibility.Collapsed;
            var id = agent.Id;
            item.Click += async (_, _) => await SetAgentFilterAsync(id);
            _filterMenu.Items.Add(item); _agentItems[id] = item;
        }
        _filterMenu.Items.Add(new MenuFlyoutSeparator());
        // The folder scope is set from a workspace's menu or a folder row's (Conversations in this folder); here it is shown, and cleared.
        _scopeItem = new ToggleMenuFlyoutItem { Text = "Every folder", IsChecked = true, Icon = new FontIcon { Glyph = ExplorerGlyph, FontFamily = Theme.Icons } };
        _scopeItem.Click += async (_, _) => await SetScopeAsync(null);
        _filterMenu.Items.Add(_scopeItem);
        _filterMenu.Items.Add(new MenuFlyoutSeparator());
        // Fold all groups while any is open, else Unfold all groups: the keyboard's way to the folds, which the headers take by pointer.
        _foldAll = new MenuFlyoutItem { Text = "Fold all groups", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
        _foldAll.Click += (_, _) => SetFolds(_sections.Select(section => section.Section.Key), fold: _sections.Any(section => !section.Header.Folded));
        _filterMenu.Opening += (_, _) =>
        {
            var anyOpen = _sections.Any(section => !section.Header.Folded);
            _foldAll.Text = anyOpen ? "Fold all groups" : "Unfold all groups";
            _foldAll.IsEnabled = _sections.Count > 0;
        };
        _filterMenu.Items.Add(_foldAll);
        _filterButton.Flyout = _filterMenu;
        Grid.SetColumn(_filterButton, 1); searchRow.Children.Add(_filterButton);
        // Refresh history, on request: the catalog is read at launch and after a tab or pane with a saved conversation closes
        // (RefreshHistoryAfterClose); a session begun elsewhere since is found with this. Settings → Local data has the same button.
        _refreshButton = _theme.IconButton("\uE72C", RefreshTooltip(null), 32, 14);
        AutomationProperties.SetName(_refreshButton, "Refresh history");
        _refreshButton.Click += async (_, _) => await RefreshHistoryAsync();
        Grid.SetColumn(_refreshButton, 2); searchRow.Children.Add(_refreshButton);
        grid.Children.Add(searchRow);
        if (_preferences.AgentFilter is { } saved && !AgentOn(saved)) _preferences.AgentFilter = null;

        // One line for the filters in force and the count, with a clear button while a filter is set. The folder scope's own
        // chip and the count's "newest first" went with the bar.
        var stateRow = new Grid { ColumnSpacing = 6 };
        stateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _historyCount = _theme.Tertiary("");
        stateRow.Children.Add(_historyCount);
        _clearFilters = _theme.IconButton("\uE894", "Show every agent and folder", 24, 10);
        AutomationProperties.SetName(_clearFilters, "Clear the filters");
        _clearFilters.Click += async (_, _) => { _preferences.AgentFilter = null; SavePreferences(); RefreshAgentFilter(); await SetScopeAsync(null); };
        Grid.SetColumn(_clearFilters, 1); stateRow.Children.Add(_clearFilters);
        Grid.SetRow(stateRow, 1); grid.Children.Add(stateRow);

        _history.Background = _theme.Transparent;
        _theme.Accentuate(_history.Resources);
        _history.Margin = new Thickness(-8, 0, -8, 0);
        _history.SelectionChanged += async (_, args) =>
        {
            if (_history.SelectedItem is ListViewItem { Tag: ConversationSection })
            {
                // A header is never left selected: the click on it folds its group (BuildGroupHeader), and the row selected
                // before, if any, is selected again; that row's detail is already in it, so the selection below leaves it be.
                _history.SelectedItem = args.RemovedItems.OfType<ListViewItem>().FirstOrDefault(item => item.Tag is Conversation or SavedTab);
                return;
            }
            if (_history.SelectedItem is ListViewItem { Tag: Conversation conversation })
            {
                if (_previewSlot is null || _selectedConversation?.Key != conversation.Key) await ShowConversationPreviewAsync(conversation);
            }
            else if (_history.SelectedItem is ListViewItem { Tag: SavedTab group })
            {
                if (_previewSlot is null || _selectedGroup?.Id != group.Id) ShowGroupDetail(group);
            }
        };
        Grid.SetRow(_history, 2); grid.Children.Add(_history);

        _previewCard = BuildPreviewDetail();
        _groupDetail = BuildGroupDetail();
        RefreshAgentFilter();
        return grid;
    }

    /// <summary>
    /// The selected conversation's detail, shown inside its row rather than in a card at the panel's foot (October 8, 2026): the
    /// excerpt, a note for a transcript whose text is not searchable, Resume, its state in a tab, and a menu with the rarer ways
    /// in. One element, moved into whichever row is selected (PlacePreview), so the list keeps the whole panel and the row's
    /// own tile, title and folder are not said again.
    /// </summary>
    private Border BuildPreviewDetail()
    {
        // Room under the buttons, so Resume does not sit on the row's edge; the row's own padding ends above the slot.
        var detail = new Border { Padding = new Thickness(0, 2, 0, 10), Visibility = Visibility.Collapsed };
        var stack = new StackPanel { Spacing = 8 };
        _previewExcerpt = _theme.Caption("");
        _previewExcerpt.TextWrapping = TextWrapping.Wrap; _previewExcerpt.MaxLines = 4; _previewExcerpt.TextTrimming = TextTrimming.CharacterEllipsis; _previewExcerpt.LineHeight = 17;
        stack.Children.Add(_previewExcerpt);
        _previewNote = _theme.Tertiary("");
        _previewNote.Foreground = _theme.Warning; _previewNote.TextWrapping = TextWrapping.Wrap; _previewNote.TextTrimming = TextTrimming.None; _previewNote.Visibility = Visibility.Collapsed;
        stack.Children.Add(_previewNote);
        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _previewResume = _theme.PrimaryButton("Resume", "Open this exact conversation in a terminal (a double-click on the row does the same)");
        // A little shorter than a dialog's button: it sits inside a list row.
        _previewResume.MinHeight = 30; _previewResume.Padding = new Thickness(12, 3, 12, 4);
        _previewResume.Click += async (_, _) => { if (_selectedConversation is { } conversation) await ResumeConversationAsync(conversation, new("the Resume button in Sessions", DateTimeOffset.UtcNow)); };
        _previewResume.DoubleTapped += (_, args) => args.Handled = true;
        actions.Children.Add(_previewResume);
        (_previewState, _previewStateDot, _previewStateText) = StateLabel();
        Grid.SetColumn(_previewState, 1); actions.Children.Add(_previewState);
        var open = _theme.IconButton("\uE712", "Open as a tab or in a split, or copy the session ID", 30, 14);
        AutomationProperties.SetName(open, "More ways to open");
        open.DoubleTapped += (_, args) => args.Handled = true;
        var openMenu = new MenuFlyout();
        var asTab = new MenuFlyoutItem { Text = "Open as tab", Icon = new FontIcon { Glyph = "\uE8A7", FontFamily = Theme.Icons } };
        asTab.Click += async (_, _) => { if (_selectedConversation is { } conversation) await OpenConversationAsync(conversation); };
        var asSplit = new MenuFlyoutItem { Text = "Open in split of current tab" };
        asSplit.Click += async (_, _) => { if (_selectedConversation is { } conversation) await OpenConversationAsync(conversation, split: true); };
        var resumeSplit = new MenuFlyoutItem { Text = "Resume in split of current tab" };
        resumeSplit.Click += async (_, _) => { if (_selectedConversation is { } conversation) await ResumeConversationAsync(conversation, new("Resume in split of current tab, on the Open menu in Sessions", DateTimeOffset.UtcNow), split: true); };
        var copyId = new MenuFlyoutItem { Text = "Copy session ID", Icon = new FontIcon { Glyph = "\uE8C8", FontFamily = Theme.Icons } };
        copyId.Click += (_, _) =>
        {
            if (_selectedConversation is not { } conversation) return;
            // A failure here would leave the click handler and end SicTerm; the clipboard held past the retries is reported.
            try { ClipboardWrite.SetText(conversation.SessionId); }
            catch (Exception exception) { ShowError("The session ID was not copied: " + exception.Message); }
        };
        openMenu.Items.Add(asTab); openMenu.Items.Add(asSplit); openMenu.Items.Add(resumeSplit); openMenu.Items.Add(new MenuFlyoutSeparator()); openMenu.Items.Add(copyId);
        open.Flyout = openMenu;
        Grid.SetColumn(open, 2); actions.Children.Add(open);
        stack.Children.Add(actions);
        detail.Child = stack;
        return detail;
    }

    /// <summary>The row slot holding the detail now; a Border's Child does not report the Border as its Parent, so it is kept here.</summary>
    private Border? _previewSlot;

    /// <summary>Moves the conversation detail into the selected row, out of whichever row held it, and shows it.</summary>
    private void PlacePreview() => PlaceDetail(_previewCard);

    /// <summary>Moves a detail, the conversation's or the pinned tab's, into the selected row's slot.</summary>
    private void PlaceDetail(Border card)
    {
        DetachPreview();
        if (_history.SelectedItem is ListViewItem { Content: StackPanel body } && body.Children.Count == 2 && body.Children[1] is Border slot)
        {
            slot.Child = card;
            _previewSlot = slot;
            card.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// The selected pinned tab's detail, inside its row as a conversation's is: its panes, each with its agent's tile, name and
    /// folder, then Resume, its state when its tab is open, and a menu with Open as tab and Unpin.
    /// </summary>
    private Border BuildGroupDetail()
    {
        var detail = new Border { Padding = new Thickness(0, 2, 0, 10), Visibility = Visibility.Collapsed };
        var stack = new StackPanel { Spacing = 8 };
        _groupPanes = new StackPanel { Spacing = 4 };
        stack.Children.Add(_groupPanes);
        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _groupResume = _theme.PrimaryButton("Resume", "Open the whole tab and resume every pane in it (a double-click on the row does the same)");
        _groupResume.MinHeight = 30; _groupResume.Padding = new Thickness(12, 3, 12, 4);
        _groupResume.Click += async (_, _) => { if (_selectedGroup is { } group) await ResumeGroupAsync(group, new("the Resume button on a pinned tab in Sessions", DateTimeOffset.UtcNow)); };
        _groupResume.DoubleTapped += (_, args) => args.Handled = true;
        actions.Children.Add(_groupResume);
        (_groupState, _groupStateDot, _groupStateText) = StateLabel();
        Grid.SetColumn(_groupState, 1); actions.Children.Add(_groupState);
        var open = _theme.IconButton("", "Open the tab without resuming it, or unpin it", 30, 14);
        AutomationProperties.SetName(open, "More ways to open");
        open.DoubleTapped += (_, args) => args.Handled = true;
        var openMenu = new MenuFlyout();
        var asTab = new MenuFlyoutItem { Text = "Open as tab", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
        asTab.Click += (_, _) => { if (_selectedGroup is { } group) OpenGroup(group); };
        var unpin = new MenuFlyoutItem { Text = "Unpin", Icon = new FontIcon { Glyph = Theme.UnpinGlyph, FontFamily = Theme.Icons } };
        unpin.Click += async (_, _) => { if (_selectedGroup is { } group) await UnpinGroupAsync(group); };
        openMenu.Items.Add(asTab); openMenu.Items.Add(new MenuFlyoutSeparator()); openMenu.Items.Add(unpin);
        open.Flyout = openMenu;
        Grid.SetColumn(open, 2); actions.Children.Add(open);
        stack.Children.Add(actions);
        detail.Child = stack;
        return detail;
    }

    /// <summary>Fills the pinned tab's detail for <paramref name="group"/> and puts it in the selected row.</summary>
    private void ShowGroupDetail(SavedTab group)
    {
        _selectedGroup = group; _selectedConversation = null;
        _previewCancellation?.Cancel();
        _groupPanes.Children.Clear();
        var names = _preferences.ConversationNames;
        foreach (var pane in group.Panes)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tile = _theme.AgentTile(pane.AgentId, 16); tile.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(tile);
            var name = _theme.BodyText(TabLabels.WithoutAgentName(TabLabels.PaneTitle(pane, names)), 12); name.MaxWidth = 180;
            Grid.SetColumn(name, 1); row.Children.Add(name);
            var folder = _theme.Tertiary(pane.WorkingDirectory.Length > 0 ? Paths.Leaf(pane.WorkingDirectory) : "Folder unknown");
            ToolTipService.SetToolTip(folder, pane.WorkingDirectory.Length > 0 ? pane.WorkingDirectory : null);
            Grid.SetColumn(folder, 2); row.Children.Add(folder);
            _groupPanes.Children.Add(row);
        }
        PlaceDetail(_groupDetail);
        RefreshGroupState(group);
    }

    /// <summary>The detail's state chip and Resume for a pinned tab: open or running when its tab is, and Go to tab once every pane runs.</summary>
    private void RefreshGroupState(SavedTab group)
    {
        var host = HostOfGroup(group);
        var running = host is not null && host.Panes.Any(pane => pane.IsRunning);
        _groupState.Visibility = host is null ? Visibility.Collapsed : Visibility.Visible;
        PaintStateLabel(_groupStateDot, _groupStateText, running);
        _groupResume.Content = host is not null && host.Panes.All(pane => pane.IsRunning) ? "Go to tab" : "Resume";
    }

    /// <summary>
    /// A pinned tab's row under Pinned (docs/decisions/tab-groups.md): the tab's color as a bar at the row's left edge, its first
    /// pane's tile, its name, "Pinned tab · n panes · folder", the dot while its tab is open, and the filled pin, which unpins it.
    /// A double-click resumes the whole tab; the right-click menu has Resume, Open as tab and Unpin.
    /// </summary>
    private ListViewItem BuildGroupRow(SavedTab group)
    {
        var names = _preferences.ConversationNames;
        var content = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The color bar stands in the row's own left padding, so the tile stays in line with the conversation rows' tiles.
        if (ResolveColor(EffectiveColor(group), _theme.StripDark) is { } hex)
        {
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Theme.Parse(hex)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-7, 0, 0, 0) };
            Grid.SetColumnSpan(bar, 3); content.Children.Add(bar);
        }
        var tile = _theme.AgentTile(group.Panes[0].AgentId, 24); tile.VerticalAlignment = VerticalAlignment.Top; tile.Margin = new Thickness(0, 1, 0, 0);
        content.Children.Add(tile);
        var text = new StackPanel { Spacing = 2 };
        var title = _theme.BodyText(TabLabels.TabTitle(group, names), 13);
        title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        text.Children.Add(_theme.Tertiary("Pinned tab · " + TabGroups.Summary(group)));
        Grid.SetColumn(text, 1); content.Children.Add(text);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        var slot = new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        SetGroupMark(slot, group);
        _groupMarks.Add((group, slot));
        marks.Children.Add(slot);
        marks.Children.Add(PinButton(true, () => UnpinGroupAsync(group)));
        Grid.SetColumn(marks, 2); content.Children.Add(marks);
        var body = new StackPanel();
        body.Children.Add(content);
        body.Children.Add(new Border { Margin = new Thickness(34, 0, 6, 0) });
        var row = new ListViewItem { Content = body, Tag = group, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 0, 4, 0), MinHeight = 52 };
        ToolTipService.SetToolTip(row, string.Join("\n", group.Panes.Select(pane => TabLabels.PaneTitle(pane, names) + (pane.WorkingDirectory.Length > 0 ? " · " + pane.WorkingDirectory : ""))));
        row.DoubleTapped += async (_, _) => await ResumeGroupAsync(group, new("a double-click on the pinned tab in Sessions", DateTimeOffset.UtcNow));
        row.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            ShowGroupMenu(row, group, args.TryGetPosition(row, out var point) ? point : null);
        };
        return row;
    }

    /// <summary>The dot that says the pinned tab is open, running or failed, or nothing while it is closed; set in the row's slot as a conversation's is.</summary>
    private void SetGroupMark(Border slot, SavedTab group)
    {
        var host = HostOfGroup(group);
        if (host is null) { slot.Child = null; slot.Visibility = Visibility.Collapsed; ToolTipService.SetToolTip(slot, null); return; }
        var (state, brush) = host.Panes.Any(pane => pane.IsRunning) ? ("Running", _theme.Success) : host.Panes.Any(pane => pane.Status == PaneStatus.Failed) ? ("Error", _theme.Danger) : ("Open", _theme.Neutral);
        if (slot.Child is not Microsoft.UI.Xaml.Shapes.Ellipse dot) slot.Child = dot = Theme.Dot(brush, 6);
        dot.Fill = brush;
        slot.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(slot, state + " in a tab");
    }

    private void ShowGroupMenu(ListViewItem row, SavedTab group, Windows.Foundation.Point? position)
    {
        var menu = new MenuFlyout();
        void Item(string text, string glyph, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontFamily = Theme.Icons } };
            item.Click += async (_, _) => { try { await action(); } catch (Exception exception) { ShowError(exception.Message); } };
            menu.Items.Add(item);
        }
        Item("Resume", "", () => ResumeGroupAsync(group, new("Resume on the pinned tab's right-click menu", DateTimeOffset.UtcNow)));
        Item("Open as tab", "", () => { OpenGroup(group); return Task.CompletedTask; });
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Unpin", Theme.UnpinGlyph, () => UnpinGroupAsync(group));
        menu.ShowAt(row, position is { } point ? new FlyoutShowOptions { Position = point } : new FlyoutShowOptions());
    }

    /// <summary>Takes the detail out of the row that holds it, before that row goes or another is selected.</summary>
    private void DetachPreview()
    {
        if (_previewSlot is null) return;
        _previewSlot.Child = null;
        _previewSlot = null;
    }

    /// <summary>Puts the view menu's checks and the state line in step with the saved grouping, sort, agent filter and folder scope.</summary>
    private void RefreshAgentFilter()
    {
        foreach (var (id, item) in _agentItems) item.IsChecked = id == (_preferences.AgentFilter ?? "");
        foreach (var (word, item) in _groupingItems) item.IsChecked = word == _preferences.GroupBy;
        foreach (var (word, item) in _sortItems) item.IsChecked = word == _preferences.SortBy;
        RefreshListState();
    }

    /// <summary>The view menu's grouping: saved, named on the state line, and the list arranged again.</summary>
    internal async Task SetGroupingAsync(string word)
    {
        word = ConversationListing.NormalizeGrouping(word);
        if (word == _preferences.GroupBy) { RefreshAgentFilter(); return; }
        _preferences.GroupBy = word;
        SavePreferences();
        RefreshAgentFilter();
        await SearchAsync();
    }

    /// <summary>The view menu's sort: saved, named on the state line, and the list arranged again.</summary>
    internal async Task SetSortAsync(string word)
    {
        word = ConversationListing.NormalizeSort(word);
        if (word == _preferences.SortBy) { RefreshAgentFilter(); return; }
        _preferences.SortBy = word;
        SavePreferences();
        RefreshAgentFilter();
        await SearchAsync();
    }

    /// <summary>Folds a group down to its header, or unfolds it: saved by the section's key, and the rows hidden or shown in place.</summary>
    internal void ToggleFold(string key)
    {
        // A fold made among a search's matches lasts for the search and is not saved: the whole list keeps its own folds.
        if (_searchSections) { if (!_searchFolds.Remove(key)) _searchFolds.Add(key); ApplyFolds(); return; }
        SetFolds([key], fold: !_preferences.FoldedSections.Contains(key));
    }

    /// <summary>Whether the sections showing are a search's matches by workspace, not the whole list, and the sections folded among them.</summary>
    private bool _searchSections;
    private readonly HashSet<string> _searchFolds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folds or unfolds the given sections, saves the folds, and shows the list accordingly.</summary>
    internal void SetFolds(IEnumerable<string> keys, bool fold)
    {
        var folded = _preferences.FoldedSections;
        foreach (var key in keys) folded = SectionFolds.With(folded, key, fold);
        _preferences.FoldedSections = folded;
        SavePreferences();
        ApplyFolds();
    }

    /// <summary>Hides the rows of every folded section and shows the rest, with each header's chevron saying which it is.</summary>
    private void ApplyFolds()
    {
        foreach (var (section, _, header, rows) in _sections)
        {
            // A search's sections start open, whatever is folded in the whole list: a match is not hidden under a fold made while browsing.
            var folded = _searchSections ? _searchFolds.Contains(section.Key) : _preferences.FoldedSections.Contains(section.Key);
            header.Folded = folded;
            foreach (var row in rows) row.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>The filter menu's agent choice: saved, shown on the state line, and the list searched again.</summary>
    private async Task SetAgentFilterAsync(string? filter)
    {
        if (filter == _preferences.AgentFilter) { RefreshAgentFilter(); return; }
        _preferences.AgentFilter = filter;
        if (filter is not null) _preferences.PreferredAgent = filter;
        SavePreferences();
        RefreshAgentFilter();
        await SearchAsync();
    }

    /// <summary>A listed folder's tree, set up; choosing a folder in one tree clears the selection in every other, so one row is chosen at a time.</summary>
    private void ConfigureFolderTree(TreeView tree)
    {
        tree.Background = _theme.Transparent;
        tree.Margin = new Thickness(-8, 0, -8, 0);
        // Left to itself the tree presents a node's content only as data: a row built as an element showed the node's type name.
        // The template presents the node's Content through the converter, which gives each container a folder row element of
        // its own.
        var resources = Application.Current.Resources;
        if (!resources.ContainsKey(nameof(FolderRowPresenter))) resources[nameof(FolderRowPresenter)] = new FolderRowPresenter();
        tree.ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><ContentPresenter Content=\"{Binding Content, Converter={StaticResource "
            + nameof(FolderRowPresenter) + "}}\" /></DataTemplate>");
        tree.Expanding += FolderExpanding;
        tree.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItem is not TreeViewNode node || !_folderPaths.TryGetValue(node, out var path)) return;
            foreach (var other in _folderTrees.Values) if (!ReferenceEquals(other, tree)) other.SelectedNode = null;
            SelectFolder(path);
        };
    }

    /// <summary>
    /// A folder chosen in the Folders panel, a listed folder's row or a folder in its tree: the New session dialog starts there, from
    /// the rail, the strip's + and the empty window's New session alike, until a workspace is switched to or a pane is clicked
    /// (<see cref="ForgetSelectedFolder"/>). The latest of the three decides: kept for good, a folder chosen once outlived the
    /// workspaces switched to after it (Chris, November 9, 2026, from the empty window's New session).
    /// </summary>
    private void SelectFolder(string path) => _selectedFolder = _explorerFolder = path;

    /// <summary>Lets go of the folder chosen in the Folders panel, and of its trees' highlight, so the New session dialog starts in the focused pane's folder or the workspace's.</summary>
    private void ForgetSelectedFolder()
    {
        _selectedFolder = null; _explorerFolder = null;
        foreach (var tree in _folderTrees.Values) tree.SelectedNode = null;
    }

    /// <summary>The folder the last Open in File Explorer went to. The smoke reads it, and records it rather than open a window on the desktop.</summary>
    internal string? LastExplorerFolder { get; private set; }

    /// <summary>
    /// Opens a folder of a workspace's tree, or a workspace's home, in File Explorer. A folder gone since it was listed says so, where Explorer would
    /// quietly open its default folder instead.
    /// </summary>
    private void OpenInExplorer(string folder)
    {
        if (!Directory.Exists(folder)) { ShowError("That folder no longer exists: " + folder); return; }
        LastExplorerFolder = folder;
        if (_options.SmokeTest) return;
        try { Paths.OpenInExplorer(folder); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowError(exception.Message); }
    }

    /// <summary>
    /// Ctrl+Shift+E: the folder chosen in a panel (<see cref="_explorerFolder"/>), else the focused pane's working folder, else the
    /// current workspace's own folder, in File Explorer.
    /// </summary>
    private void OpenChosenFolder()
    {
        if (_explorerFolder is { } chosen) OpenInExplorer(chosen);
        else if (SelectedHost is { } host) OpenPaneFolder(FocusedPane(host));
        else if (_workspace?.Home is { } home) OpenInExplorer(home);
    }

    /// <summary>The pane's working folder in File Explorer, as its header's folder button opens it. The smoke records it instead.</summary>
    private void OpenPaneFolder(SessionPane pane)
    {
        if (_options.SmokeTest) { LastExplorerFolder = pane.FolderToOpen; return; }
        pane.OpenFolder();
    }

    private async Task SetScopeAsync(string? folder)
    {
        _preferences.ScopeFolder = ConversationCatalog.NormalizeScope(folder) is null ? null : Path.TrimEndingDirectorySeparator(folder!.Trim());
        SavePreferences();
        RefreshListState();
        await SearchAsync();
    }

    /// <summary>
    /// The line under the search: the agent and the folder in force, the grouping unless it is by date and the sort unless it is
    /// newest first, then the count ("Claude Code · in repos · by workspace · 4 matches"), the clear button while an agent or a
    /// folder is set, and the view menu's folder item, which names the scope and clears it.
    /// </summary>
    private void RefreshListState()
    {
        if (_historyCount is null) return;
        var scope = _preferences.ScopeFolder;
        var parts = new List<string>();
        if (_preferences.AgentFilter is { } filter) parts.Add(Theme.AgentName(filter));
        if (scope is not null) parts.Add("in " + Paths.Leaf(scope));
        if (_preferences.GroupBy == ConversationListing.ByFolder) parts.Add("by workspace");
        else if (_preferences.GroupBy == ConversationListing.ByAgent) parts.Add("by agent");
        if (_preferences.SortBy == ConversationListing.ByName) parts.Add("by name");
        if (_countText.Length > 0) parts.Add(_countText);
        _historyCount.Text = string.Join(" · ", parts);
        ToolTipService.SetToolTip(_historyCount, scope is null ? null : "Only conversations under " + scope);
        _clearFilters.Visibility = _preferences.AgentFilter is null && scope is null ? Visibility.Collapsed : Visibility.Visible;
        if (_scopeItem is null) return;
        _scopeItem.Text = scope is null ? "Every folder" : "Only in " + Paths.Leaf(scope);
        _scopeItem.IsChecked = true;
        _scopeItem.IsEnabled = scope is not null;
    }

    private readonly SingleFlight<bool> _detect = new();

    /// <summary>When the last detection finished; MinValue before one has.</summary>
    private DateTimeOffset _detectedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Detection after the one under way, if any: that one may have looked before what asked for this one (an install, a
    /// command saved in Settings), and two at once would only race each other.
    /// </summary>
    private async Task DetectToolsAsync()
    {
        if (_detect.Current is { } underway) await underway;
        await _detect.RunAsync(DetectOnceAsync);
    }

    private async Task<bool> DetectOnceAsync()
    {
        _detecting = true;
        RefreshAgentRows();
        try
        {
            // The probe inherits SicTerm's variables, which may predate an install (EnvironmentRefresh).
            EnvironmentRefresh.Apply();
            var detected = await AgentDetection.ProbeAsync(_preferences.Agents);
            _detectionFailed = false;
            // What each command resolved to, for a PC where a tool is "detected" that was never installed.
            Diagnostics.Log("agent detection: " + string.Join("; ", detected.Select(result => result.Summary())));
            foreach (var result in detected)
            {
                _available[result.Id] = result.Available;
                _detection[result.Id] = result;
            }
            await RefreshAgentIconsAsync();
            // An undecided agent (every agent on a first run, one added since the settings were saved, one not installed until
            // now) is turned on once it is found.
            if (BuiltInAgents.Decide(_preferences.Agents, id => detected.FirstOrDefault(result => result.Id == id)?.Available) is { } decided)
            {
                // An agent installed from Get set up's tab is said by that install's own notice, with its buttons (MainWindow.Setup.cs).
                var turnedOn = decided.Where(agent => agent.IsOn && !AgentOn(agent.Id) && !_installs.Any(install => install.Item.Id == agent.Id)).Select(agent => agent.Name).ToList();
                _preferences.Agents = decided;
                SavePreferences();
                _ = ApplyAgentSwitchesAsync(refreshHistory: true);
                // The welcome's card names the agents on a first run (marked seen as its film starts, so the film itself is the
                // test); any later find is said once.
                if (!WelcomeDue && _welcomeFilm is null && turnedOn.Count > 0) NotifyBriefly(turnedOn.Count == 1 ? "Agent turned on" : "Agents turned on", Names(turnedOn) + " detected on this PC.");
            }
            return true;
        }
        catch (Exception exception)
        {
            _detection.Clear();
            _detectionFailed = true;
            ShowError(exception.Message);
            return false;
        }
        finally
        {
            _detecting = false;
            _detectedAt = DateTimeOffset.UtcNow;
            RefreshAgentRows();
            SendWelcomeContext();
            RefreshSetupAdvice();
        }
    }

    private readonly SingleFlight<bool> _refresh = new();

    /// <summary>
    /// Re-reads every agent's history into the catalog. A request made while a refresh runs waits for that one: it used to
    /// return at once, so a resume that needed the refreshed catalog could check the old one instead.
    /// </summary>
    private Task RefreshHistoryAsync() => _closing ? Task.CompletedTask : _refresh.RunAsync(RefreshOnceAsync);

    /// <summary>A refresh after the one under way, if any: a close during a refresh may have been passed over by it.</summary>
    private async Task RefreshHistoryAfterCurrentAsync()
    {
        if (_refresh.Current is { } underway) await underway;
        await RefreshHistoryAsync();
    }

    /// <summary>The Refresh button's tooltip, with the time of the last refresh once there has been one.</summary>
    private static string RefreshTooltip(string? stamp) => "Refresh history" + (stamp is null ? "" : " · last refreshed " + stamp);

    /// <summary>
    /// A new session the history now holds is a saved conversation like any other: its pane no longer starts again when its
    /// history is not found (<see cref="FreshStart"/>), its placeholder offers Resume, and it can be pinned (PinnableKey). The
    /// catalog is asked once for every such pane, off the UI thread.
    /// </summary>
    private async Task NoteSavedSessionsAsync()
    {
        var unsaved = AllPanes.Where(pane => pane.Descriptor is { Unsaved: true, SessionId: not null }).ToList();
        if (unsaved.Count == 0) return;
        var keys = unsaved.Select(pane => pane.ConversationKey!).Distinct(StringComparer.Ordinal).ToList();
        var held = await Task.Run(() => _catalog.Find(keys).Select(record => record.Key).ToHashSet(StringComparer.Ordinal));
        if (_closing) return;
        var saved = false;
        foreach (var pane in unsaved)
        {
            // The pane may have been closed, or changed identity, while the catalog answered.
            if (pane.ConversationKey is not { } key || !held.Contains(key) || !pane.Descriptor.Unsaved || HostOf(pane) is null) continue;
            pane.UpdateIdentity(pane.Descriptor with { Unsaved = false });
            if (pane.Terminal is null) ShowSavedPreview(pane, key);
            saved = true;
        }
        if (saved) { SaveLayout(); RefreshPinMarks(); }
    }

    private async Task<bool> RefreshOnceAsync()
    {
        UpdateStatus("Refreshing local history…");
        if (_refreshButton is not null) _refreshButton.IsEnabled = false;
        try
        {
            // An agent switched off, or not yet settled by detection, is not read; what the catalog holds of it stays.
            var definitions = _preferences.Agents.Where(agent => agent.IsOn).ToArray();
            var result = await Task.Run(() => _indexer.Refresh(definitions, _lifetime.Token));
            if (_closing) return false;
            await NoteSavedSessionsAsync();
            await SearchAsync();
            // Windows' own short time, as the limits and the update check show it: 9:51 PM on a 12-hour clock.
            var stamp = DateTime.Now.ToString("t");
            HistoryRefreshes++;
            if (_refreshButton is not null) ToolTipService.SetToolTip(_refreshButton, RefreshTooltip(stamp));
            UpdateStatus($"History refreshed {stamp} · {result.Imported} imported"
                + (result.MetadataOnly > 0 ? $" · {result.MetadataOnly} metadata only" : "")
                + (result.Failed > 0 ? $" · {result.Failed} source issues" : ""));
            if (result.Failed > 0)
                Notify(InfoBarSeverity.Warning, result.Failed == 1 ? "One history file was skipped" : result.Failed + " history files were skipped",
                    string.Join("\n", result.Warnings.Take(8).Select(ShortenWarning)) + (result.Failed > 8 ? "\n" + (result.Failed - 8) + " more not shown." : "") + "\nThe original files were not changed.");
            NoteUnlistedSessions(result.Unlisted ?? [], noticed: result.Failed == 0);
            // The first Pin common folders waits for a refresh that read an agent: on a first run the agents are undecided until
            // detection answers, so the refresh started beside it reads none and imports nothing, and the one detection then
            // starts has them. The welcome's card learns the facts the same way (MainWindow.Welcome.cs).
            if (definitions.Length > 0) { _refreshesWithAgents++; await FirstSuggestionsAsync(); }
            SendWelcomeContext();
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception exception) { if (!_closing) ShowError(exception.Message); return false; }
        finally { if (_refreshButton is not null) _refreshButton.IsEnabled = true; }
    }

    private async Task SearchAsync(bool debounce = false)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var cancellation = _searchCancellation.Token;
        var query = _search.Text.Length > 4096 ? _search.Text[..4096] : _search.Text;
        var filter = _preferences.AgentFilter; var scope = _preferences.ScopeFolder; var hidden = HiddenAgents();
        var names = new Dictionary<string, string>(_preferences.ConversationNames, StringComparer.Ordinal);
        // The pinned tabs the list shows (docs/decisions/tab-groups.md): those the agent filter and the folder scope admit, and with a query, those it names.
        var groups = _preferences.TabGroups.Where(group => TabGroups.Shown(group, filter, scope, hidden) && TabGroups.Matches(group, query, names)).ToList();
        var grouping = _preferences.GroupBy; var sort = _preferences.SortBy;
        // The folder groups roll up to the listed folders (the pinned folders and the Workplaces list until November 8, 2026).
        var roots = Folders;
        try
        {
            if (debounce) await Task.Delay(180, cancellation);
            var pinned = new HashSet<string>(_preferences.PinnedConversations, StringComparer.Ordinal);
            // Without a query the list is grouped (docs/decisions/sessions-groups.md): Pinned, then the folders, the days or the
            // agents. A pin older than the newest page is fetched on its own so it never drops out of Pinned. A query lists
            // matches as the catalog ranks them, without groups. The arranging reads each conversation's folder from the open
            // panes too (FolderOf), so it is done here, on the UI thread; five hundred rows take a moment.
            var (found, older) = await Task.Run(() =>
            {
                var page = _catalog.Search(query, filter, aliases: names, folderScope: scope, hiddenAgents: hidden);
                if (!string.IsNullOrWhiteSpace(query)) return (page, (IReadOnlyList<Conversation>?)null);
                var listed = page.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
                return (page, _catalog.Find(pinned.Where(key => !listed.Contains(key)).Order(StringComparer.Ordinal), filter, scope, names, hidden));
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            // A search by workspace keeps its matches in the catalog's order under each workspace (November 12, 2026, at Chris's
            // request); by date and by agent it stays the one ranked list.
            _searchSections = older is null;
            if (!_searchSections) _searchFolds.Clear();
            var sections = older is not null ? ConversationGroups.Arrange(found.Concat(older), pinned, DateTimeOffset.Now, TimeZoneInfo.Local, grouping, sort, FolderOf, roots, Theme.AgentName)
                : grouping == ConversationListing.ByFolder ? ConversationGroups.Arrange(found, pinned, DateTimeOffset.Now, TimeZoneInfo.Local, grouping, sort, FolderOf, roots, Theme.AgentName, ranked: true)
                : null;
            // The same answer as the list shows leaves the rows as they are: a refresh that found nothing new, a pin elsewhere, a
            // search typed and corrected. Rebuilding is up to 500 rows of elements on the UI thread, a stall of a few hundred
            // milliseconds in the middle of typing or of closing a tab; the rows' and headers' times and pane dots are brought up to date instead.
            var signature = ListSignature(found, sections, pinned, groups, names);
            if (signature == _listSignature)
            {
                foreach (var (conversation, _, meta, prefix) in _rowMarks)
                {
                    var line = prefix + Paths.RelativeTime(conversation.UpdatedAt);
                    if (meta.Text != line) meta.Text = line;
                }
                foreach (var (section, _, header, _) in _sections) header.Note = SectionNote(section);
                RefreshConversationRows();
            }
            else
            {
                _listSignature = signature;
                var selectedKey = _selectedConversation?.Key;
                var selectedGroupId = _selectedGroup?.Id;
                // The detail sits inside a row; the rows go, so it comes out first and goes back into the row selected again.
                DetachPreview();
                _history.Items.Clear();
                _rowMarks.Clear(); _groupMarks.Clear(); _sections.Clear();
                foreach (var label in _groupLabels) _displayText.Remove(label);
                _groupLabels.Clear();
                ListViewItem? reselect = null;
                List<ListViewItem>? rows = null;
                void Header(ConversationSection section)
                {
                    var (row, header) = BuildGroupHeader(section, first: _history.Items.Count == 0);
                    _history.Items.Add(row);
                    rows = [];
                    _sections.Add((section, row, header, rows));
                }
                void Add(Conversation conversation, ConversationSection? section)
                {
                    var row = BuildConversationRow(conversation, section);
                    _history.Items.Add(row); rows?.Add(row);
                    if (conversation.Key == selectedKey) reselect = row;
                }
                void AddGroup(SavedTab group)
                {
                    var row = BuildGroupRow(group);
                    _history.Items.Add(row); rows?.Add(row);
                    if (group.Id == selectedGroupId) reselect = row;
                }
                if (sections is null)
                {
                    // A query lists the pinned tabs it names first, then the conversations as the catalog ranks them.
                    foreach (var group in groups) AddGroup(group);
                    foreach (var conversation in found) Add(conversation, null);
                }
                else
                {
                    // The pinned tabs lead the Pinned group, under its header even when no conversation is pinned.
                    if (groups.Count > 0)
                    {
                        Header(ConversationSection.PinnedHeader);
                        foreach (var group in groups) AddGroup(group);
                    }
                    foreach (var section in sections)
                    {
                        if (section.Group != ConversationGroup.Pinned || groups.Count == 0) Header(section);
                        foreach (var conversation in section.Conversations) Add(conversation, section);
                    }
                }
                ApplyFolds();
                if (reselect is not null) _history.SelectedItem = reselect;
            }
            var count = sections?.Sum(section => section.Conversations.Count) ?? found.Count;
            // The count alone; the state line puts the agent and the folder in force before it.
            _countText = found.Count == 500 ? "Newest 500 · refine your search"
                : count == 0 ? (query.Length > 0 ? "No matches" : filter is null && scope is null ? "No imported conversations yet" : "No conversations")
                : query.Length > 0 ? (count == 1 ? "1 match" : count + " matches")
                : count == 1 ? "1 conversation" : count + " conversations";
            RefreshListState();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!_closing) ShowError(exception.Message); }
    }

    /// <summary>What the list last built its rows from; null until it has built them once.</summary>
    private string? _listSignature;

    /// <summary>
    /// Everything a row and the detail inside it read from a listing: each conversation's record (its body is empty in a
    /// listing), the folder it would resume in, whether it is pinned, and the groups the rows fall under. Two listings with the
    /// same signature would build the same rows.
    /// </summary>
    private string ListSignature(IReadOnlyList<Conversation> found, IReadOnlyList<ConversationSection>? sections, IReadOnlySet<string> pinned,
        IReadOnlyList<SavedTab> groups, IReadOnlyDictionary<string, string> names)
    {
        var signature = new System.Text.StringBuilder();
        foreach (var group in groups) signature.Append('\u001c').Append(TabGroups.Signature(group, names)).Append('\u001e');
        void Row(Conversation conversation) => signature.Append(conversation).Append('\u001f').Append(FolderOf(conversation)).Append('\u001f').Append(pinned.Contains(conversation.Key)).Append('\u001e');
        if (sections is null) foreach (var conversation in found) Row(conversation);
        else foreach (var section in sections)
        {
            signature.Append('\u001d').Append(section.Key).Append('\u001f').Append(section.Label).Append('\u001f').Append(SectionMark(section)).Append('\u001f').Append(SectionColor(section)).Append('\u001e');
            foreach (var conversation in section.Conversations) Row(conversation);
        }
        return signature.ToString();
    }

    /// <summary>A folder section's emoji, while its folder is a workspace's home and has one; null otherwise.</summary>
    private string? SectionMark(ConversationSection section) =>
        section.Folder is { Length: > 0 } folder && IsFolder(folder) ? _preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(folder)) : null;

    /// <summary>A folder section's tab color as RRGGBB, while its folder is a workspace's home and has one; null otherwise.</summary>
    private string? SectionColor(ConversationSection section) =>
        section.Folder is { Length: > 0 } folder && IsFolder(folder) ? ResolveColor(_preferences.FolderColor.GetValueOrDefault(PinnedFolders.Normalize(folder)), _theme.StripDark) : null;

    /// <summary>The note at a header's right end: when a folder or agent section was last written. The days say it themselves.</summary>
    private static string SectionNote(ConversationSection section) =>
        section.Group is ConversationGroup.Folder or ConversationGroup.Agent && section.Conversations.Count > 0 ? Paths.RelativeTime(section.Newest) : "";

    /// <summary>
    /// A group header in the list, set off from the rows around it: a <see cref="SectionHeader"/> in primary ink at 12 px where the
    /// rows' captions are quieter, a faint rule across the list above every group but the first, outside the header's hover, and more room above it than below so
    /// it belongs to what follows. A tap on it folds the group (docs/decisions/sessions-groups.md); it is no tab stop, so
    /// keyboard navigation passes over it, and it is never left selected (the list's SelectionChanged hands the selection back).
    /// </summary>
    private (ListViewItem Row, SectionHeader Header) BuildGroupHeader(ConversationSection section, bool first)
    {
        var header = new SectionHeader(_theme, section.Label) { Count = section.Conversations.Count, Note = SectionNote(section), Mark = SectionMark(section) };
        header.Ink = SectionInk(section);
        _groupLabels.Add(header.Label); _displayText.Add(header.Label);
        // The hover is the header line's alone, as a row's is in the other panels: the rule above belongs to no row.
        var line = new Border { Child = header, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(-6, 0, -6, 0), Background = _theme.Transparent };
        line.PointerEntered += (_, _) => line.Background = _theme.Hover;
        line.PointerExited += (_, _) => line.Background = _theme.Transparent;
        line.PointerCanceled += (_, _) => line.Background = _theme.Transparent;
        var content = new StackPanel { Spacing = 8 };
        if (!first) content.Children.Add(_theme.Divider());
        content.Children.Add(line);
        var row = new ListViewItem
        {
            Content = content, Tag = section, IsTabStop = false, MinHeight = 0,
            Padding = new Thickness(10, first ? 8 : 14, 10, 4), HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        // The list's own highlight would lie over the rule and the room above it too (Chris, November 12, 2026: "looks odd").
        foreach (var key in GroupHeaderFills) row.Resources[key] = _theme.Transparent;
        if (section.Folder is { Length: > 0 } folder) ToolTipService.SetToolTip(row, folder);
        row.Tapped += (_, _) => ToggleFold(section.Key);
        AutomationProperties.SetName(row, section.Label);
        return (row, header);
    }

    private static readonly string[] GroupHeaderFills = ["ListViewItemBackgroundPointerOver", "ListViewItemBackgroundPressed", "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed"];

    /// <summary>A folder section's color as its header's ink, the one its rows wear in the other panels (<see cref="RowColor"/>); null without one.</summary>
    private Brush? SectionInk(ConversationSection section) =>
        section.Folder is { Length: > 0 } folder && IsFolder(folder) && RowColor(folder) is { } color ? RowBar(color) : null;

    /// <summary>The headers' ink again, for a theme whose panel reads a color differently.</summary>
    private void PaintGroupHeaders()
    {
        foreach (var entry in _sections) entry.Header.Ink = SectionInk(entry.Section);
    }

    private bool IsPinned(Conversation conversation) => _preferences.PinnedConversations.Contains(conversation.Key);

    /// <summary>A row's pin: the drawn mark in the accent while pinned (see <see cref="PinMark"/>), the outline glyph otherwise. A double-click on it stays on it.</summary>
    private Button PinButton(bool pinned, Func<Task> toggle)
    {
        var pin = pinned ? _theme.MarkButton(PinMark.Create(_theme.Accent, 12), "Unpin", 24) : _theme.IconButton(PinGlyph, "Pin to the top", 24, 12, _theme.TextTertiary);
        pin.Click += async (_, _) => { try { await toggle(); } catch (Exception exception) { ShowError(exception.Message); } };
        pin.DoubleTapped += (_, args) => args.Handled = true;
        return pin;
    }

    /// <summary>Pins or unpins a conversation, saves the set, and rebuilds the list so the row moves to its group.</summary>
    internal async Task TogglePinAsync(Conversation conversation)
    {
        if (!_preferences.PinnedConversations.Remove(conversation.Key)) _preferences.PinnedConversations.Add(conversation.Key);
        SavePreferences();
        RefreshPinMarks();
        await SearchAsync();
    }

    private void ShowConversationMenu(ListViewItem row, Conversation conversation, Windows.Foundation.Point? position)
    {
        var menu = new MenuFlyout();
        void Item(string text, string glyph, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontFamily = Theme.Icons } };
            item.Click += async (_, _) => { try { await action(); } catch (Exception exception) { ShowError(exception.Message); } };
            menu.Items.Add(item);
        }
        Item("Resume", "\uE768", () => ResumeConversationAsync(conversation, new("Resume on the conversation's right-click menu", DateTimeOffset.UtcNow)));
        Item("Open as tab", "\uE8A7", async () => await OpenConversationAsync(conversation));
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(IsPinned(conversation) ? "Unpin" : "Pin to the top", IsPinned(conversation) ? Theme.UnpinGlyph : PinGlyph, () => TogglePinAsync(conversation));
        Item("Copy session ID", "\uE8C8", () =>
        {
            ClipboardWrite.SetText(conversation.SessionId);
            return Task.CompletedTask;
        });
        menu.ShowAt(row, position is { } point ? new FlyoutShowOptions { Position = point } : new FlyoutShowOptions());
    }

    /// <summary>
    /// A conversation's row. Under a folder section, its folder line names only the part under the section's folder ("docs\welcome"),
    /// or nothing at the folder itself, since the header names the folder; elsewhere it names the folder's leaf, or Folder unknown.
    /// </summary>
    private ListViewItem BuildConversationRow(Conversation conversation, ConversationSection? section)
    {
        var content = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = _theme.AgentTile(conversation.AgentId, 24); tile.VerticalAlignment = VerticalAlignment.Top; tile.Margin = new Thickness(0, 1, 0, 0);
        content.Children.Add(tile);
        var text = new StackPanel { Spacing = 2 };
        var title = _theme.BodyText(DisplayName(conversation.Title), 13);
        title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        var directory = FolderOf(conversation);
        var folder = directory is null ? ConversationGroups.UnknownFolderLabel : Paths.Leaf(directory);
        if (section is { Group: ConversationGroup.Folder, Folder: { } root }) folder = root.Length > 0 ? ConversationGroups.Under(directory, root) : section.Label == ConversationGroups.UnknownFolderLabel ? "" : folder;
        var metaPrefix = (folder.Length > 0 ? folder + " · " : "") + Theme.AgentName(conversation.AgentId) + " · ";
        var meta = _theme.Tertiary(metaPrefix + Paths.RelativeTime(conversation.UpdatedAt));
        text.Children.Add(meta);
        if (!conversation.TextIndexed) { var partial = _theme.Tertiary("First message only · text not searchable"); partial.Foreground = _theme.Warning; text.Children.Add(partial); }
        Grid.SetColumn(text, 1); content.Children.Add(text);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        // The dot's slot stays in the row; the dot is set in it now and again on every pane event (RefreshConversationRows).
        var slot = new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        SetPaneMark(slot, conversation);
        _rowMarks.Add((conversation, slot, meta, metaPrefix));
        marks.Children.Add(slot);
        // A pinned row always shows its filled pin; any other row shows an outline pin only under the pointer. The button keeps
        // its place either way, so the title does not rewrap as the pointer moves over the list.
        var pinned = IsPinned(conversation);
        var pin = PinButton(pinned, () => TogglePinAsync(conversation));
        void ShowPin(bool shown) { pin.Opacity = pinned || shown ? 1 : 0; pin.IsHitTestVisible = pinned || shown; }
        ShowPin(false);
        marks.Children.Add(pin);
        Grid.SetColumn(marks, 2); content.Children.Add(marks);
        // Under the row's own lines, a slot for the detail while the row is selected (PlacePreview), set in under the tile.
        var body = new StackPanel();
        body.Children.Add(content);
        body.Children.Add(new Border { Margin = new Thickness(34, 0, 6, 0) });
        var row = new ListViewItem { Content = body, Tag = conversation, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 0, 4, 0), MinHeight = 52 };
        ToolTipService.SetToolTip(row, conversation.WorkingDirectory ?? "No saved working folder");
        row.PointerEntered += (_, _) => ShowPin(true);
        row.PointerExited += (_, _) => ShowPin(false);
        row.DoubleTapped += async (_, _) => await ResumeConversationAsync(conversation, new("a double-click on the conversation in Sessions", DateTimeOffset.UtcNow));
        row.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            ShowConversationMenu(row, conversation, args.TryGetPosition(row, out var point) ? point : null);
        };
        return row;
    }

    /// <summary>
    /// The conversation each listed row is for, the slot its pane dot goes in, and its folder-agent-time line with the part
    /// before the time, in the list's order; emptied with the list. The time is written again when a search leaves the rows standing.
    /// </summary>
    private readonly List<(Conversation Conversation, Border Slot, TextBlock Meta, string MetaPrefix)> _rowMarks = [];

    /// <summary>
    /// The dot that says the conversation is open in a tab, running or failed, or nothing when it is not open; set in the row's
    /// slot without touching the rest of the row, so the rows, their selection and the detail inside the selected one stay put.
    /// </summary>
    private void SetPaneMark(Border slot, Conversation conversation)
    {
        var open = FindPane(conversation);
        if (open is null) { slot.Child = null; slot.Visibility = Visibility.Collapsed; ToolTipService.SetToolTip(slot, null); return; }
        var (state, brush) = open.IsRunning ? ("Running", _theme.Success) : open.Status == PaneStatus.Failed ? ("Error", _theme.Danger) : ("Open", _theme.Neutral);
        if (slot.Child is not Microsoft.UI.Xaml.Shapes.Ellipse dot) slot.Child = dot = Theme.Dot(brush, 6);
        dot.Fill = brush;
        slot.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(slot, state + " in a tab");
    }

    /// <summary>Brings every row's pane dot up to date after a pane started, failed, closed, moved or was linked. The rows themselves are kept.</summary>
    private void RefreshConversationRows()
    {
        foreach (var (conversation, slot, _, _) in _rowMarks) SetPaneMark(slot, conversation);
        foreach (var (group, slot) in _groupMarks) SetGroupMark(slot, group);
        if (_selectedConversation is { } shown) RefreshPreviewState(shown);
        if (_selectedGroup is { } selectedGroup) RefreshGroupState(selectedGroup);
    }

    private async Task ShowConversationPreviewAsync(Conversation conversation)
    {
        _selectedConversation = conversation; _selectedGroup = null;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var cancellation = _previewCancellation.Token;
        _previewExcerpt.Text = "Loading transcript…";
        _previewNote.Text = conversation.TextNote ?? "";
        _previewNote.Visibility = conversation.TextIndexed ? Visibility.Collapsed : Visibility.Visible;
        PlacePreview();
        RefreshPreviewState(conversation);
        try
        {
            // The excerpt is the first real request, which is at the start of nearly every transcript: the start is read, and
            // the whole body only when the start was cut before a request that settles the excerpt was found.
            var excerpt = await Task.Run(() =>
            {
                if (_catalog.GetBodyStart(conversation.Key, ExcerptScan) is not { } start) return Excerpt(null);
                var (text, settled) = ExcerptOf(start.Text, start.Truncated);
                return !start.Truncated || settled ? text : Excerpt(_catalog.Get(conversation.Key)?.Body);
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            _previewExcerpt.Text = excerpt;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _previewExcerpt.Text = "Transcript unavailable: " + exception.Message; }
    }

    /// <summary>
    /// The state beside Resume when the conversation or pinned tab has a tab here: a dot and plain text, not a chip, so it is not
    /// taken for a button (the owner read the chip "Open in a tab" as one, November 27, 2026).
    /// </summary>
    private (Border Host, Microsoft.UI.Xaml.Shapes.Ellipse Dot, TextBlock Text) StateLabel()
    {
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { FontSize = 11, FontFamily = Theme.Body, Foreground = _theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(dot);
        line.Children.Add(text);
        var host = new Border
        {
            Child = line, Padding = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left
        };
        return (host, dot, text);
    }

    private void PaintStateLabel(Microsoft.UI.Xaml.Shapes.Ellipse dot, TextBlock text, bool running)
    {
        text.Text = running ? "Running" : "In a tab · not running";
        dot.Fill = running ? _theme.SuccessDot : _theme.TextTertiary;
    }

    /// <summary>How much of a transcript the preview card reads first for its excerpt.</summary>
    private const int ExcerptScan = 256 * 1024;

    private void RefreshPreviewState(Conversation conversation)
    {
        var pane = FindPane(conversation);
        var running = pane?.IsRunning == true;
        _previewState.Visibility = pane is null ? Visibility.Collapsed : Visibility.Visible;
        PaintStateLabel(_previewStateDot, _previewStateText, running);
        _previewResume.Content = running ? "Go to terminal" : "Resume";
        // Going to a running terminal needs no folder; a resume needs one SIC knows, as the resume itself checks.
        var resumable = running || FolderOf(conversation) is not null;
        _previewResume.IsEnabled = resumable;
        ToolTipService.SetToolTip(_previewResume, resumable
            ? "Open this exact conversation in a terminal. Double-clicking the row does the same."
            : "Neither the saved history nor SicTerm knows the folder this conversation ran in. Open it as a tab and use Link to choose one.");
    }

    /// <summary>
    /// The folder a conversation resumes in: the agent's own record first, then the folder SIC started or linked it in, then
    /// the folder of a pane it is open in. Null when none of them knows; nothing is guessed.
    /// </summary>
    private string? FolderOf(Conversation conversation) => KnownFolder(conversation.WorkingDirectory, conversation.Key)
        ?? (FindPane(conversation)?.Descriptor.WorkingDirectory is { Length: > 0 } open ? open : null);

    /// <summary>The folder the history recorded, or else the one SIC kept for the conversation; null when neither has one.</summary>
    private string? KnownFolder(string? recorded, string key) => recorded is { Length: > 0 } ? recorded
        : _preferences.ConversationFolders.GetValueOrDefault(key) is { Length: > 0 } kept ? kept : null;


    /// <summary>Hermes names sessions the app creates after their creation token; show those as untitled instead of the token.</summary>
    private static string DisplayName(string title) => TabLabels.Display(title);

    /// <summary>The first real user message: skips slash-command records and tool XML so the card shows what the conversation was about.</summary>
    private static string Excerpt(string? body) => ExcerptOf(body, truncated: false).Text;

    /// <summary>
    /// <see cref="Excerpt"/> on a body that may be the start of a longer one: <c>Settled</c> says the answer came from a user
    /// request that ended within the text given, so the rest of the transcript could not change it.
    /// </summary>
    private static (string Text, bool Settled) ExcerptOf(string? body, bool truncated)
    {
        if (string.IsNullOrWhiteSpace(body)) return ("No message text was imported for this conversation.", false);
        string? fallback = null;
        var segments = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var newline = segment.IndexOf('\n');
            var speaker = newline >= 0 ? segment[..newline] : segment;
            var text = (newline >= 0 ? segment[(newline + 1)..] : "").TrimStart();
            if (speaker != "You" && !TabLabels.AgentNames.Contains(speaker)) { fallback ??= segment; continue; }
            if (text.Length == 0 || text.StartsWith('<') || text.StartsWith('/')) continue;
            fallback ??= text;
            // The last segment of a cut body may go on past the cut, so only one that ended before it settles the excerpt.
            if (speaker == "You") return (Collapse(text), !truncated || index < segments.Length - 1);
        }
        return (Collapse(fallback ?? body), false);
    }

    private static string Collapse(string text)
    {
        var collapsed = Regex.Replace(text, @"\s+", " ").Trim();
        return collapsed.Length > 360 ? collapsed[..360] + "…" : collapsed;
    }

    /// <summary>"C:\…\project\file.jsonl: message" instead of the full source path in an import notice.</summary>
    /// <summary>
    /// Logs each session a refresh left out for an id in a form SicTerm does not resume, and says so once a run in an
    /// informational notice, unless <paramref name="noticed"/> is false because a skipped file's warning has the notice. The
    /// database's other sessions were read; nothing of the agent's was changed (docs/decisions/unlisted-sessions.md).
    /// </summary>
    private void NoteUnlistedSessions(IReadOnlyList<(AgentDefinition Agent, string SessionId)> unlisted, bool noticed)
    {
        var fresh = unlisted.Where(item => _unlistedSessions.Add(item.Agent.Id + ":" + item.SessionId)).ToList();
        if (fresh.Count == 0) return;
        foreach (var (agent, id) in fresh) Diagnostics.Log($"history: {agent.Name} session {id} not listed, its id in a form SicTerm does not resume");
        if (!noticed) return;
        var names = fresh.Select(item => item.Agent.Name).Distinct(StringComparer.Ordinal).ToList();
        var named = names.Count == 1 ? names[0] + " " : "";
        var one = fresh.Count == 1;
        Notify(InfoBarSeverity.Informational, (one ? "One " : fresh.Count + " ") + named + (one ? "session" : "sessions") + " not listed",
            (one ? "Its session ID is" : "Their session IDs are") + " in a form SicTerm does not read yet, which usually follows "
            + (names.Count == 1 ? "a " + names[0] : "an agent") + " update. The other sessions are listed, "
            + (one ? "the ID is" : "the IDs are") + " in sic.log, and no files were changed.");
    }

    private static string ShortenWarning(string warning)
    {
        var separator = warning.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? Paths.Shorten(warning[..separator]) + ": " + warning[(separator + 2)..] : warning;
    }
    /// <summary>
    /// Windows' folder picker over this window: the folder's path, or null when cancelled. The Windows App SDK picker, not
    /// Windows.Storage.Pickers: those do not work in a process running as administrator (Windows App SDK 1.8 release notes),
    /// which is how Add folder came to answer "That did not work" on an installed copy (October 1, 2026). With <paramref name="start"/>
    /// the picker opens in that folder, or the nearest folder above it that exists, so a subfolder of the folder a session was
    /// about to start in is one click away (the New session dialog's Browse; asked for by Chris on November 8, 2026); without, where
    /// Windows last left it.
    /// </summary>
    private async Task<string?> PickFolderAsync(string? start = null)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
        if (NearestFolder(start) is { } from) picker.SuggestedFolder = from;
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>The folder itself when it exists, else the nearest one above it that does; null for no path, one that is not a full path, or none found.</summary>
    internal static string? NearestFolder(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim())) return null;
            for (var folder = Path.TrimEndingDirectorySeparator(path.Trim()); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(folder)) return folder;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        return null;
    }

    private void Forget(TreeViewNode node)
    {
        _folderPaths.Remove(node); _folderMarks.Remove(node);
        foreach (var child in node.Children) Forget(child);
    }

    /// <summary>The emoji for a working folder: the nearest marked listed folder's (<see cref="FolderMarks.For"/>), or null.</summary>
    internal string? FolderMarkFor(string? folder) => FolderMarks.For(Folders, _preferences.FolderEmoji, folder);

    /// <summary>
    /// A listed folder's emoji: saved, and shown at once on its rows in both panels and on the indicator at the head of the strip
    /// (docs/decisions/folder-emoji.md). A mark that is not one text element, or null, clears it.
    /// </summary>
    internal void SetFolderEmoji(string folder, string? mark)
    {
        _preferences.FolderEmoji = FolderMarks.With(_preferences.FolderEmoji, folder, mark);
        SavePreferences();
        RepaintFolder(folder);
        RefreshFolderMarks();
    }

    /// <summary>
    /// Focuses the unseen emoji box, for <paramref name="folder"/>, and opens Windows' own emoji panel over
    /// it by sending the Win + . chord (<see cref="EmojiPanel"/>), since no API opens it for a desktop window. The smoke focuses
    /// the box only: its session may be locked, where sent input is refused.
    /// </summary>
    internal async Task OpenEmojiPanelAsync(string folder)
    {
        _emojiFolder = folder;
        ClearFolderEmojiBox();
        // A control that is no tab stop takes no focus; the box is one until it loses the focus again.
        _folderEmojiBox.IsTabStop = true;
        _folderEmojiBox.Focus(FocusState.Programmatic);
        if (_options.SmokeTest) return;
        // The panel attaches to the control focused when the chord arrives; a moment lets the focus settle.
        await Task.Delay(80);
        if (!EmojiPanel.Open()) NotifyBriefly("Folder emoji", "The emoji panel did not open. Win + . opens it.");
    }

    /// <summary>
    /// The unseen box Windows' emoji panel types into, held by the window's root: 24 px, never drawn, a tab stop only while
    /// it takes the panel's emoji. TextChanging, not TextChanged: the box is never drawn, and TextChanged did not come for it
    /// (October 13, 2026). The box is emptied once the change is done, for the next emoji; Escape or Enter ends the choosing,
    /// back on the rail's Folders button, and the box leaves the tab order with the focus.
    /// </summary>
    private TextBox BuildFolderEmojiBox()
    {
        _folderEmojiBox = new TextBox
        {
            Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Opacity = 0,
            IsHitTestVisible = false, IsTabStop = false, MaxLength = FolderMarks.MaxLength, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(_folderEmojiBox, "Folder emoji");
        _folderEmojiBox.TextChanging += (_, _) =>
        {
            if (_syncingFolderEmoji || _emojiFolder is not { } folder || FolderMarks.Last(_folderEmojiBox.Text) is not { } mark) return;
            SetFolderEmoji(folder, mark);
            DispatcherQueue.TryEnqueue(ClearFolderEmojiBox);
        };
        _folderEmojiBox.KeyDown += (_, args) =>
        {
            if (args.Key is not (Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Enter)) return;
            args.Handled = true;
            _railItems["folders"].Button.Focus(FocusState.Keyboard);
        };
        _folderEmojiBox.LostFocus += (_, _) => _folderEmojiBox.IsTabStop = false;
        return _folderEmojiBox;
    }

    /// <summary>Empties the emoji box without taking that for a choice, so the next emoji chosen is the only one in it.</summary>
    private void ClearFolderEmojiBox()
    {
        _syncingFolderEmoji = true;
        try { _folderEmojiBox.Text = ""; } finally { _syncingFolderEmoji = false; }
    }

    /// <summary>Repaints every row of a folder in the trees, and both panels' rows, whose marks read the list.</summary>
    private void RepaintFolder(string path)
    {
        foreach (var (node, repaint) in _folderMarks.ToArray())
            if (_folderPaths.TryGetValue(node, out var other) && PinnedFolders.Same(other, path)) repaint();
        RefreshWorkspaceStates();
    }

    /// <summary>Puts every pane's folder mark in step: none since November 8, 2026, when the emoji moved to the folders' rows and the strip's indicator, so a pane that had one loses it.</summary>
    private void RefreshFolderMarks() { foreach (var pane in AllPanes) pane.RefreshFolderMark(); }

    /// <summary>
    /// A folder's node. Its content is the row's data, which <see cref="FolderRowPresenter"/> turns into an element for each
    /// container that shows it. <paramref name="exists"/> is given for a folder a listing has just found, so it is not asked again.
    /// </summary>
    private TreeViewNode FolderNode(string label, string path, bool? exists = null)
    {
        var node = new TreeViewNode { HasUnrealizedChildren = exists ?? Directory.Exists(path) };
        node.Content = new FolderRowData(label, () => FolderRow(node, label, path));
        _folderPaths[node] = path;
        return node;
    }

    /// <summary>
    /// A folder row as the tree node's content: the row element last built for it, and a way to build another. A node's content
    /// was once the element itself, and an element can be in one container only; a collapse leaves the rows in their old
    /// containers, so the next expand could not place them and showed them blank, every other time.
    /// </summary>
    private sealed class FolderRowData(string label, Func<FrameworkElement> build)
    {
        /// <summary>The row element built last, the one the tree shows; null until a container first asks for it.</summary>
        public FrameworkElement? View { get; private set; }

        /// <summary>The row for a container to show: the last one built while no container holds it, or a new one.</summary>
        public FrameworkElement Present()
        {
            if (View is null || VisualTreeHelper.GetParent(View) is not null) View = build();
            return View;
        }

        /// <summary>The folder name, which is what the tree item reads out when it describes its content.</summary>
        public override string ToString() => label;
    }

    /// <summary>The folder trees' item template converter: a row element for <see cref="FolderRowData"/>, and any other content as it is.</summary>
    private sealed partial class FolderRowPresenter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) => value is FolderRowData row ? row.Present() : value;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    /// <summary>The row element the tree shows for a folder node, or null for a row not shown yet.</summary>
    private static FrameworkElement? FolderRowView(TreeViewNode node) => (node.Content as FolderRowData)?.View;

    /// <summary>
    /// A folder row in a listed folder's tree: its emoji while it is itself listed with one, its name, a badge counting the open
    /// sessions in it (<see cref="BuildBadge"/>). Nothing else: an Open in File Explorer button sat at the row's right edge, under the pointer only, until
    /// November 12, 2026, when Chris chose a clean row with the badge alone; the row's menu and Ctrl+Shift+E open the folder.
    /// </summary>
    private Grid FolderRow(TreeViewNode node, string label, string path)
    {
        var content = new Grid { ColumnSpacing = 2, MinHeight = 24 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The folder's emoji, while it is listed and has one, then the name; in one stretching column, so the badge keeps its place.
        var label_ = new Grid { ColumnSpacing = 4 };
        label_.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label_.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mark = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        label_.Children.Add(mark);
        var name = _theme.BodyText(label, 14); name.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(name, 1); label_.Children.Add(name);
        content.Children.Add(label_);
        var badge = BuildBadge(() => SessionsIn(path));
        Grid.SetColumn(badge.Badge, 1); content.Children.Add(badge.Badge);
        ToolTipService.SetToolTip(content, path);
        AutomationProperties.SetName(content, label);
        content.ContextFlyout = FolderMenu(path);
        void Paint()
        {
            var emoji = IsFolder(path) ? _preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(path)) : null;
            mark.Text = emoji ?? "";
            mark.Visibility = emoji is null ? Visibility.Collapsed : Visibility.Visible;
            PaintBadge(badge, SessionsIn(path));
        }
        Paint();
        _folderMarks[node] = Paint;
        return content;
    }

    /// <summary>
    /// A folder row's right-click menu, built as it opens: New session here, New folder, Add to Folders (a workspace of its own for the folder,
    /// not shown for one already listed), Conversations in this folder, and Open in File Explorer.
    /// </summary>
    private MenuFlyout FolderMenu(string path)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var start = new MenuFlyoutItem { Text = "New session here", Icon = new FontIcon { Glyph = "\uE756", FontFamily = Theme.Icons } };
            start.Click += async (_, _) => await ShowNewSessionAsync(path);
            menu.Items.Add(start);
            menu.Items.Add(NewFolderItem(path));
            if (!IsFolder(path))
            {
                var add = new MenuFlyoutItem { Text = "Add to Folders", Icon = new FontIcon { Glyph = FolderGlyph, FontFamily = Theme.Icons } };
                ToolTipService.SetToolTip(add, "A workspace of its own: sessions in this folder and under it go there rather than to the folder above.");
                add.Click += (_, _) => { try { AddFolder(path); NotifyBriefly("Added to Folders", Paths.Leaf(path)); } catch (Exception exception) { ShowError(exception.Message); } };
                menu.Items.Add(add);
            }
            var conversations = new MenuFlyoutItem { Text = "Conversations in this folder", Icon = new FontIcon { Glyph = "\uE71C", FontFamily = Theme.Icons } };
            conversations.Click += async (_, _) => { await SetScopeAsync(path); OpenPanel("sessions"); };
            menu.Items.Add(conversations);
            menu.Items.Add(new MenuFlyoutSeparator());
            var open = new MenuFlyoutItem { Text = "Open in File Explorer", Icon = new FontIcon { Glyph = ExplorerGlyph, FontFamily = Theme.Icons } };
            open.Click += (_, _) => OpenInExplorer(path);
            menu.Items.Add(open);
        };
        return menu;
    }

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }

    /// <summary>The folders a listing leaves out: version control, dependencies and build output.</summary>
    private static readonly HashSet<string> SkippedFolders = new([".git", "node_modules", ".venv", "__pycache__", ".vs", "bin", "obj", ".obsidian"], StringComparer.OrdinalIgnoreCase);
    private const int MaxChildFolders = 1000;

    /// <summary>
    /// A folder row expands: its child folders are listed again, every time, so a folder made since the last look shows up and one
    /// deleted goes, without a restart. Up to October 14, 2026 a folder was listed on its first expand only, and kept that list
    /// for the rest of the run.
    /// </summary>
    private void FolderExpanding(TreeView sender, TreeViewExpandingEventArgs args) => _ = RelistFolderAsync(args.Node, quiet: false);

    /// <summary>
    /// Lists a folder node's child folders off the UI thread, since a network or WSL folder can take seconds to answer, and brings
    /// its rows in line (<see cref="ReconcileFolderRows"/>); then does the same for the subfolders under it that are still open,
    /// which show their own lists as they stand. <paramref name="quiet"/> is a refresh nobody asked for: a folder it cannot read
    /// keeps its rows and says nothing, where an expand shows the reason.
    /// </summary>
    private async Task RelistFolderAsync(TreeViewNode node, bool quiet)
    {
        if (!_folderPaths.TryGetValue(node, out var path)) return;
        string[]? folders;
        try { folders = await Task.Run(() => ChildFolders(path)); }
        catch (DirectoryNotFoundException) { folders = null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (quiet || !_folderPaths.ContainsKey(node)) return;
            if (node.Children.Count == 0) node.HasUnrealizedChildren = false;
            ShowError(exception.Message);
            return;
        }
        // The tree was emptied, its folder taken off the list, while the folder was read.
        if (!_folderPaths.ContainsKey(node)) return;
        var first = node.Children.Count == 0;
        ReconcileFolderRows(node, folders is null ? [] : folders.Take(MaxChildFolders).ToArray());
        node.HasUnrealizedChildren = false;
        if (!quiet && folders is null) ShowError("That folder no longer exists: " + path);
        else if (!quiet && first && folders!.Length > MaxChildFolders)
            Notify(InfoBarSeverity.Informational, "Large folder", "Showing the first 1,000 subfolders. Any path can be typed in the New session dialog.");
        foreach (var open in node.Children.Where(child => child.IsExpanded).ToArray()) await RelistFolderAsync(open, quiet: true);
    }

    /// <summary>
    /// A folder's child folders as the trees list them: the <see cref="SkippedFolders"/> left out, sorted with a tie-break so two
    /// listings of the same folders come out in the same order, and one more than are shown, so a cut list is told; null for a
    /// folder that no longer exists.
    /// </summary>
    private static string[]? ChildFolders(string path)
    {
        if (!Directory.Exists(path)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        return Directory.EnumerateDirectories(path, "*", options).Where(folder => !SkippedFolders.Contains(Path.GetFileName(folder)))
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ThenBy(folder => folder, StringComparer.Ordinal).Take(MaxChildFolders + 1).ToArray();
    }

    /// <summary>
    /// Brings a folder node's rows in line with a fresh listing, in place: the rows of folders gone are dropped, rows for new
    /// folders go in their sorted places, and the rest are kept as they are, so a subfolder left open stays open. No
    /// Children.Clear(): clearing even an empty list collapses the node mid-expand. A kept row that is shut and has no rows, a
    /// folder found empty when it was last opened, gets its chevron back, for a folder made in it since.
    /// </summary>
    private void ReconcileFolderRows(TreeViewNode node, IReadOnlyList<string> folders) => ReconcileFolderRows(node.Children, folders);

    /// <summary>The same, for any list of rows: a node's children, or a tree's root nodes, which are a listed folder's subfolders.</summary>
    private void ReconcileFolderRows(IList<TreeViewNode> rows, IReadOnlyList<string> folders)
    {
        var listed = new HashSet<string>(folders, StringComparer.Ordinal);
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (_folderPaths.TryGetValue(rows[index], out var kept) && listed.Contains(kept)) continue;
            Forget(rows[index]);
            rows.RemoveAt(index);
        }
        // The kept rows are already in the listing's order, so a folder not found at its place is a new one.
        for (var index = 0; index < folders.Count; index++)
        {
            if (index < rows.Count && _folderPaths.GetValueOrDefault(rows[index]) == folders[index]) continue;
            rows.Insert(index, FolderNode(Path.GetFileName(folders[index]), folders[index], exists: true));
        }
        foreach (var child in rows) if (!child.IsExpanded && child.Children.Count == 0) child.HasUnrealizedChildren = true;
    }
}
