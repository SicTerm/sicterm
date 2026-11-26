using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

namespace Sic.App;

/// <summary>
/// The Folders panel: where sessions start (docs/decisions/workspaces.md). A row per folder in the Folders list, each with its
/// emoji or the folder glyph, its name, a badge counting the open sessions in it, and under it a tree of its subfolders (kept
/// through the rows' rebuilds so open folders stay open; relisted on opening, on the panel opening and on the window coming back
/// to the front). A folder row anywhere starts a session with New session here, and a subfolder can join the list with Add to
/// Folders; a folder with open sessions carries a badge with their count and most urgent state, and a click on it shows the
/// first. The folder's menu has its emoji, color and sound, its place in the list, and Remove from Folders. The Workplaces panel
/// did this until November 8, 2026, then the workspace rows for a day.
/// </summary>
internal sealed partial class MainWindow
{
    private StackPanel _folderList = null!;
    private readonly Dictionary<string, FolderRootView> _folderRows = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each listed folder's tree, by the folder as the list keeps it, kept through the rows' rebuilds so its open folders stay open; and which have been listed.</summary>
    private readonly Dictionary<string, TreeView> _folderTrees = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folderTreesListed = new(StringComparer.OrdinalIgnoreCase);
    private string _foldersPanelSignature = "";
    /// <summary>The open panes by their working folder, keyed as the list keys folders, for the badges (<see cref="RefreshFolderBadges"/>).</summary>
    private Dictionary<string, List<SessionPane>> _folderSessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A listed folder's row and the tree under it, with the parts that change.</summary>
    private sealed class FolderRootView
    {
        internal required string Folder { get; init; }
        internal required StackPanel Container { get; init; }
        internal required Grid Row { get; init; }
        internal required FontIcon Chevron { get; init; }
        internal required TextBlock Mark { get; init; }
        internal required FontIcon Glyph { get; init; }
        internal required TextBlock Name { get; init; }
        internal required (Border Badge, Ellipse Dot, TextBlock Count) Badge { get; init; }
        internal required TreeView Tree { get; init; }
        internal bool Hovered { get; set; }
    }

    /// <summary>Verification: the listed folders' rows and trees, by folder.</summary>
    internal TreeView? FolderTreeOf(string folder) => _folderTrees.GetValueOrDefault(PinnedFolders.Normalize(folder));
    internal Grid? FolderRowOf(string folder) => _folderRows.GetValueOrDefault(PinnedFolders.Normalize(folder))?.Row;
    internal (Border Badge, Ellipse Dot, TextBlock Count)? FolderBadgeOf(string folder) => _folderRows.GetValueOrDefault(PinnedFolders.Normalize(folder))?.Badge;
    internal TextBlock? FolderRootMarkOf(string folder) => _folderRows.GetValueOrDefault(PinnedFolders.Normalize(folder))?.Mark;

    /// <summary>Whether a listed folder's row is folded, its tree hidden; open until folded (<see cref="SectionFolds.Root"/>).</summary>
    internal bool FolderFolded(string folder) => _preferences.FoldedSections.Contains(SectionFolds.Root(folder));

    /// <summary>Folds or opens a listed folder's row, saved by key; the tree is listed again as it opens.</summary>
    internal void ToggleFolderFold(string folder)
    {
        var fold = !FolderFolded(folder);
        _preferences.FoldedSections = SectionFolds.With(_preferences.FoldedSections, SectionFolds.Root(folder), fold);
        SavePreferences();
        RefreshFoldersPanel();
        if (!fold && _folderRows.TryGetValue(PinnedFolders.Normalize(folder), out var view)) _ = RelistRootAsync(view, quiet: false);
    }

    /// <summary>
    /// The Folders panel: a line of tools at the top right (Sort and Add Folder), then a row per listed folder with its tree, each
    /// row dragged to put the list in order. The panel's own title names it, so the list carries no header, and no description under
    /// it. A history button sat before Add folder until November 9, 2026, adding the folders used most; Chris found it confusing
    /// once the list was set up, and the first run's suggestions (<see cref="FirstSuggestionsAsync"/>) are what is left of it.
    /// </summary>
    private UIElement BuildFoldersPanel()
    {
        var grid = new Grid { Padding = new Thickness(12, 2, 12, 12), RowSpacing = 4 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        var sort = _theme.IconButton(SortGlyph, "Sort", 28, 13);
        AutomationProperties.SetName(sort, "Sort");
        sort.Flyout = FolderSortMenu();
        tools.Children.Add(sort);
        var add = _theme.IconButton(NewFolderGlyph, "Add Folder", 28, 13);
        AutomationProperties.SetName(add, "Add Folder");
        add.Click += async (_, _) => await AddFolderFromPickerAsync();
        tools.Children.Add(add);
        grid.Children.Add(tools);
        // A folder made in File Explorer shows up once SIC's window is back in front (RefreshOpenFolders; the panel's opening is the other moment).
        Activated += (_, args) => { if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshOpenFolders(); };
        _folderList = new StackPanel { Spacing = 2, Padding = new Thickness(0, 0, 0, PanelFootBuffer) };
        _folderScroll = new ScrollViewer { Content = WithDropLine(_folderList, _folderDropLine), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(-6, 0, -6, 0) };
        Grid.SetRow(_folderScroll, 1); grid.Children.Add(_folderScroll);
        return grid;
    }

    /// <summary>Add Folder's glyph: a folder with a plus (NewFolder, E8F4); an open folder (E8DA) until November 9, 2026.</summary>
    internal const string NewFolderGlyph = "";
    /// <summary>Sort's glyph (E8CB), the one the Sessions list's group-and-sort button wears.</summary>
    internal const string SortGlyph = "";

    private ScrollViewer _folderScroll = null!;
    private readonly Border _folderDropLine = new() { Height = 2, CornerRadius = new CornerRadius(1), Visibility = Visibility.Collapsed, IsHitTestVisible = false };

    /// <summary>The listed folders' rows in the Folders panel, in the list's order: where a dragged row may land.</summary>
    private IReadOnlyList<(string Folder, FrameworkElement Container, FrameworkElement Row)> FolderDragRows() => Folders
        .Where(_folderRows.ContainsKey).Select(folder => (folder, (FrameworkElement)_folderRows[folder].Container, (FrameworkElement)_folderRows[folder].Row)).ToList();

    /// <summary>
    /// Sort's menu: the list put in an order once, by name, by the sessions open in each folder's workspace, or by the latest
    /// conversation under each folder. A sort is a rearrangement, not a mode, so a drag or Move up and down after it stands; a
    /// standing sort by open sessions would move the rows under the pointer as agents start and stop.
    /// </summary>
    private MenuFlyout FolderSortMenu()
    {
        var menu = new MenuFlyout();
        var name = new MenuFlyoutItem { Text = "Name" };
        name.Click += (_, _) => SortFolders(FolderSort.Name);
        menu.Items.Add(name);
        var open = new MenuFlyoutItem { Text = "Open sessions" };
        ToolTipService.SetToolTip(open, "Most first");
        open.Click += (_, _) => SortFolders(FolderSort.OpenSessions);
        menu.Items.Add(open);
        var recent = new MenuFlyoutItem { Text = "Recently used" };
        ToolTipService.SetToolTip(recent, "The latest conversation under each folder, newest first");
        recent.Click += async (_, _) => await SortFoldersByRecentAsync();
        menu.Items.Add(recent);
        menu.Opening += (_, _) => { foreach (var item in menu.Items) item.IsEnabled = Folders.Count > 1; };
        return menu;
    }

    internal enum FolderSort { Name, OpenSessions }

    /// <summary>Puts the list in order by name (the folder's own, then its path) or by the sessions open in each workspace, most first; ties keep their places.</summary>
    internal void SortFolders(FolderSort sort)
    {
        var folders = Folders;
        var ordered = sort == FolderSort.Name
            ? folders.OrderBy(Paths.Leaf, StringComparer.CurrentCultureIgnoreCase).ThenBy(folder => folder, StringComparer.CurrentCultureIgnoreCase).ToList()
            : folders.OrderByDescending(folder => WorkspaceOfFolder(folder) is { } workspace ? HostsOf(workspace).Where(host => !host.Closing).Sum(host => host.Panes.Count(pane => !pane.Closing)) : 0).ToList();
        SetFolderOrder(ordered);
    }

    /// <summary>
    /// Puts the list in order by the latest conversation under each folder (counted for the deepest listed folder holding it, as a
    /// tab is), newest first, folders without one last in their places. The catalog is read off the UI thread; the smoke passes its own <paramref name="activity"/>.
    /// </summary>
    internal async Task SortFoldersByRecentAsync(IReadOnlyList<(string? Folder, DateTimeOffset UpdatedAt)>? activity = null)
    {
        var folders = Folders;
        Dictionary<string, DateTimeOffset> latest;
        try
        {
            latest = await Task.Run(() =>
            {
                var found = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
                foreach (var (folder, updated) in activity ?? _catalog.FolderActivity(DateTimeOffset.MinValue))
                    if (Workspaces.RootFor(folders, folder) is { } root && (!found.TryGetValue(root, out var seen) || updated > seen)) found[root] = updated;
                return found;
            });
        }
        catch (Exception exception) { if (!_closing) ShowError("The history could not be read: " + exception.Message); return; }
        // The list as it stands now, which a folder added or removed while the history was read may have changed.
        SetFolderOrder(Folders.OrderByDescending(folder => latest.TryGetValue(PinnedFolders.Normalize(folder), out var updated) ? updated : DateTimeOffset.MinValue).ToList());
    }

    /// <summary>Everything the rows are built from: the list and the folds. A change rebuilds the rows; otherwise their states are brought up to date.</summary>
    private string FoldersPanelSignature() => string.Join('\u001e', Folders.Select(folder => folder + '\u001f' + (FolderFolded(folder) ? "-" : "+")));

    /// <summary>Brings the panel in line with the list: rows rebuilt when a folder came, went, moved or folded, their trees kept; states refreshed either way.</summary>
    internal void RefreshFoldersPanel()
    {
        if (_folderList is null) return;
        var signature = FoldersPanelSignature();
        var rebuilt = signature != _foldersPanelSignature;
        if (rebuilt)
        {
            _foldersPanelSignature = signature;
            // The trees outlive the rows: taken out of the old containers for the new ones, and dropped for folders no longer listed.
            foreach (var view in _folderRows.Values) view.Container.Children.Remove(view.Tree);
            foreach (var gone in _folderTrees.Keys.Where(key => !IsFolder(key)).ToArray())
            {
                var tree = _folderTrees[gone];
                foreach (var node in tree.RootNodes) Forget(node);
                tree.RootNodes.Clear();
                _folderTrees.Remove(gone); _folderTreesListed.Remove(gone);
            }
            _folderList.Children.Clear();
            _folderRows.Clear();
            foreach (var folder in Folders)
            {
                var view = BuildFolderRootRow(folder);
                _folderRows[folder] = view;
                _folderList.Children.Add(view.Container);
            }
        }
        RefreshFolderStates();
        // A tree shown after a rebuild reads its folder again, quietly, as the panel's opening does.
        if (rebuilt) foreach (var view in _folderRows.Values.Where(view => view.Tree.Visibility == Visibility.Visible).ToArray()) _ = RelistRootAsync(view, quiet: true);
    }

    /// <summary>The rows' marks, chevrons and badges, and the tree rows' marks and badges, from the folders' emoji and the open panes.</summary>
    private void RefreshFolderStates()
    {
        if (_folderList is null) return;
        RefreshFolderBadges();
        foreach (var view in _folderRows.Values)
        {
            var folded = FolderFolded(view.Folder);
            view.Chevron.Glyph = folded ? "" : "";
            // Put away during a row drag, the fold unchanged (BeginFolderDrag).
            view.Tree.Visibility = folded || FolderDragging ? Visibility.Collapsed : Visibility.Visible;
            var mark = _preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(view.Folder));
            view.Mark.Text = mark ?? "";
            view.Mark.Visibility = mark is null ? Visibility.Collapsed : Visibility.Visible;
            view.Glyph.Visibility = mark is null ? Visibility.Visible : Visibility.Collapsed;
            // Open, the row counts the sessions in the folder itself, its subfolders counting their own; folded, everything its workspace holds.
            PaintBadge(view.Badge, folded ? SessionsUnder(view.Folder) : SessionsIn(view.Folder));
            PaintFolderRootRow(view);
        }
        foreach (var repaint in _folderMarks.Values.ToArray()) repaint();
    }

    /// <summary>A listed folder's row: its name and its folder glyph in its color, if it has one; the row itself plain, lit under the pointer.</summary>
    private void PaintFolderRootRow(FolderRootView view)
    {
        var color = RowColor(view.Folder);
        view.Name.Foreground = color is { } ink ? RowBar(ink) : _theme.Text;
        view.Glyph.Foreground = color is { } tint ? RowBar(tint) : _theme.TextSecondary;
        view.Row.Background = view.Hovered ? _theme.Hover : _theme.Transparent;
    }

    /// <summary>Reads the open panes into <see cref="_folderSessions"/>, by working folder.</summary>
    private void RefreshFolderBadges()
    {
        var sessions = new Dictionary<string, List<SessionPane>>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in _open.Values)
        {
            if (host.Closing) continue;
            foreach (var pane in host.Panes)
            {
                if (pane.Closing || pane.Descriptor.WorkingDirectory is not { Length: > 0 } folder) continue;
                var key = PinnedFolders.Normalize(folder);
                if (!sessions.TryGetValue(key, out var list)) sessions[key] = list = [];
                list.Add(pane);
            }
        }
        _folderSessions = sessions;
    }

    /// <summary>The open panes working in the folder itself.</summary>
    private IReadOnlyList<SessionPane> SessionsIn(string folder) => _folderSessions.GetValueOrDefault(PinnedFolders.Normalize(folder)) ?? (IReadOnlyList<SessionPane>)[];

    /// <summary>
    /// The open panes a listed folder holds: in it or under it, but not under a folder listed inside it, which counts its own (the
    /// rule that places tabs, <see cref="RootFor"/>). Counting everything under it put a nested folder's session on the outer row
    /// while the outer workspace held none (Chris, November 9, 2026: a red dot on C:\Batch\Repos from a session in its listed sicterm).
    /// </summary>
    private IReadOnlyList<SessionPane> SessionsUnder(string folder) => _folderSessions.Where(entry => PinnedFolders.Same(RootFor(entry.Key), folder)).SelectMany(entry => entry.Value).ToList();

    /// <summary>A badge for a folder row: a dot in the sessions' most urgent state and their count, in a pill; hidden while there are none. A click shows the first session.</summary>
    private (Border Badge, Ellipse Dot, TextBlock Count) BuildBadge(Func<IReadOnlyList<SessionPane>> sessions)
    {
        var dot = Theme.Dot(_theme.Neutral, 6);
        var count = _theme.Tertiary(""); count.FontSize = 11; count.Foreground = _theme.Text;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(dot); content.Children.Add(count);
        var badge = new Border { Child = content, Background = _theme.Layer, CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        ToolTipService.SetToolTip(badge, "Open sessions here. Click to show the first.");
        badge.Tapped += (_, args) => { args.Handled = true; ShowSession(sessions().FirstOrDefault()); };
        badge.PointerEntered += (_, _) => badge.Background = _theme.Pressed;
        badge.PointerExited += (_, _) => badge.Background = _theme.Layer;
        return (badge, dot, count);
    }

    private void PaintBadge((Border Badge, Ellipse Dot, TextBlock Count) badge, IReadOnlyList<SessionPane> sessions)
    {
        badge.Badge.Visibility = sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (sessions.Count == 0) return;
        badge.Count.Text = sessions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var state = sessions.Select(pane => pane.PresenceState).DefaultIfEmpty(PaneState.Idle).Max();
        var running = sessions.Any(pane => pane.IsRunning);
        badge.Dot.Fill = state switch
        {
            PaneState.Blocked or PaneState.Error => _theme.DangerDot,
            PaneState.Attention or PaneState.Done => _theme.WarningDot,
            PaneState.Working => _theme.Accent,
            _ => running ? _theme.SuccessDot : _theme.Neutral
        };
    }

    /// <summary>Shows a pane's tab, switching workspace when it is in another, and gives the pane the keyboard.</summary>
    private void ShowSession(SessionPane? pane)
    {
        if (pane is null || HostOf(pane) is not { } host) return;
        ShowHost(host);
        SetActivePane(pane);
    }

    /// <summary>
    /// A listed folder's row: a chevron that folds its tree, its emoji or the folder glyph, its name, and its badge; a click folds
    /// or opens the tree, and the row's menu has the rest. The tree under it lists the folder's subfolders.
    /// </summary>
    private FolderRootView BuildFolderRootRow(string folder)
    {
        var row = new Grid { ColumnSpacing = 6, MinHeight = 30, Padding = new Thickness(4, 3, 8, 3), CornerRadius = new CornerRadius(4), Background = _theme.Transparent };
        for (var column = 0; column < 4; column++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = column == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var chevron = Theme.Icon("", 9, _theme.TextSecondary);
        var fold = new Button { Content = chevron, Width = 20, Height = 20, Padding = new Thickness(0), Background = _theme.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(fold, "Show or hide the folders in it");
        AutomationProperties.SetName(fold, "Show or hide the folders");
        fold.Click += (_, _) => ToggleFolderFold(folder);
        row.Children.Add(fold);
        var markHost = new Grid { Width = 18, VerticalAlignment = VerticalAlignment.Center };
        var mark = new TextBlock { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var glyph = Theme.Icon(FolderGlyph, 14, _theme.TextSecondary);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        markHost.Children.Add(mark); markHost.Children.Add(glyph);
        Grid.SetColumn(markHost, 1); row.Children.Add(markHost);
        var name = _theme.BodyText(Paths.Leaf(folder), 14, 600); name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 2); row.Children.Add(name);
        var badge = BuildBadge(() => FolderFolded(folder) ? SessionsUnder(folder) : SessionsIn(folder));
        Grid.SetColumn(badge.Badge, 3); row.Children.Add(badge.Badge);
        ToolTipService.SetToolTip(row, folder);
        AutomationProperties.SetName(row, "Folder " + Paths.Leaf(folder));
        var tree = FolderTreeFor(folder);
        var container = new StackPanel();
        container.Children.Add(row); container.Children.Add(tree);
        var view = new FolderRootView { Folder = folder, Container = container, Row = row, Chevron = chevron, Mark = mark, Glyph = glyph, Name = name, Badge = badge, Tree = tree };
        row.Tapped += (_, args) =>
        {
            if (FolderDragJustEnded || args.OriginalSource is DependencyObject source && Ancestor<Button>(source) is not null) return;
            // The click chooses the folder for the next New session as well as folding it, as a click in a tree does.
            ForgetSelectedFolder();
            SelectFolder(folder);
            ToggleFolderFold(folder);
        };
        MakeFolderRowDraggable(row, folder, _folderList, _folderScroll, _folderDropLine, FolderDragRows);
        row.ContextFlyout = FolderRootMenu(view);
        row.PointerEntered += (_, _) => { view.Hovered = true; PaintFolderRootRow(view); };
        row.PointerExited += (_, _) => { view.Hovered = false; PaintFolderRootRow(view); };
        return view;
    }

    /// <summary>
    /// The listed folder's menu: New session here, New folder, Conversations in this folder; its emoji, color, sound and Explorer; its place in
    /// the list; and Remove from Folders. Built as it opens so the items read the state then.
    /// </summary>
    private MenuFlyout FolderRootMenu(FolderRootView view)
    {
        var folder = view.Folder;
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var start = new MenuFlyoutItem { Text = "New session here", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
            start.Click += async (_, _) => await ShowNewSessionAsync(folder);
            menu.Items.Add(start);
            menu.Items.Add(NewFolderItem(folder));
            var conversations = new MenuFlyoutItem { Text = "Conversations in this folder", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
            conversations.Click += async (_, _) => { await SetScopeAsync(folder); OpenPanel("sessions"); };
            menu.Items.Add(conversations);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddFolderDressing(menu, folder, view.Row);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddFolderOrder(menu, folder);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(RemoveFolderItem(folder));
        };
        return menu;
    }

    /// <summary>The listed folder's tree, made once and kept through the rows' rebuilds so a folder left open stays open. Its root nodes are the folder's own subfolders.</summary>
    private TreeView FolderTreeFor(string folder)
    {
        var key = PinnedFolders.Normalize(folder);
        if (!_folderTrees.TryGetValue(key, out var tree))
        {
            tree = new TreeView { SelectionMode = TreeViewSelectionMode.Single };
            ConfigureFolderTree(tree);
            // Under the row's indent, with the tree's own left edge for the chevrons. The right edge is the list's own: 8 px past it, as until November 12, 2026, the list cut into each row's Open in File Explorer button.
            tree.Margin = new Thickness(18, 0, 0, 2);
            _folderTrees[key] = tree;
        }
        return tree;
    }

    /// <summary>
    /// Lists a listed folder's subfolders off the UI thread and brings the tree's root rows in line (<see cref="ReconcileFolderRows"/>),
    /// then the open rows under them. <paramref name="quiet"/> is a refresh nobody asked for: a folder that cannot be read keeps its
    /// rows and says nothing, where the row opening shows the reason.
    /// </summary>
    private async Task RelistRootAsync(FolderRootView view, bool quiet)
    {
        var folder = view.Folder; var tree = view.Tree; var key = PinnedFolders.Normalize(folder);
        string[]? folders;
        try { folders = await Task.Run(() => ChildFolders(folder)); }
        catch (DirectoryNotFoundException) { folders = null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { if (!quiet) ShowError(exception.Message); return; }
        // The folder left the list while it was read.
        if (!_folderTrees.TryGetValue(key, out var current) || !ReferenceEquals(current, tree)) return;
        var first = tree.RootNodes.Count == 0;
        ReconcileFolderRows(tree.RootNodes, folders is null ? [] : folders.Take(MaxChildFolders).ToArray());
        _folderTreesListed.Add(key);
        if (!quiet && folders is null) ShowError("The folder no longer exists: " + folder);
        else if (!quiet && first && folders!.Length > MaxChildFolders)
            Notify(InfoBarSeverity.Informational, "Large folder", "Showing the first 1,000 subfolders. Any path can be typed in the New session dialog.");
        foreach (var open in tree.RootNodes.Where(node => node.IsExpanded).ToArray()) await RelistFolderAsync(open, quiet: true);
    }

    /// <summary>Verification: lists a listed folder's tree now.</summary>
    internal Task RelistFolderTreeAsync(string folder) => _folderRows.TryGetValue(PinnedFolders.Normalize(folder), out var view) ? RelistRootAsync(view, quiet: false) : Task.CompletedTask;

    /// <summary>
    /// Adds the folders the history says are used most (<see cref="AutoPins"/>) to the list, after those there are, and remembers
    /// each in <see cref="AppPreferences.AutoPinnedFolders"/> so a folder removed since is not added again. The catalog is read and
    /// each folder checked off the UI thread, since a folder on a network or WSL path can take seconds to answer. The smoke passes
    /// its own <paramref name="activity"/>. Returns the folders added.
    /// </summary>
    internal async Task<IReadOnlyList<string>> AddFoldersFromHistoryAsync(IReadOnlyList<(string? Folder, DateTimeOffset UpdatedAt)>? activity = null, bool quiet = false)
    {
        var now = DateTimeOffset.Now;
        var listed = Folders; var offered = _preferences.AutoPinnedFolders.ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> chosen;
        try
        {
            chosen = await Task.Run(() => AutoPins.Choose(AutoPins.Rank(activity ?? _catalog.FolderActivity(now - AutoPins.Window), now), listed, offered, home, Directory.Exists));
        }
        catch (Exception exception) { if (!quiet && !_closing) ShowError("Common folders could not be read from the history: " + exception.Message); return []; }
        if (_closing) return [];
        // Folders added while the history was read stay as they are; one added meanwhile is not added twice.
        chosen = chosen.Where(folder => !IsFolder(folder)).Take(Math.Max(0, Workspaces.MaxWorkspaces - Folders.Count)).ToList();
        if (chosen.Count > 0)
        {
            _preferences.AutoPinnedFolders = PinnedFolders.Clean(_preferences.AutoPinnedFolders.Concat(chosen));
            SavePreferences();
            foreach (var folder in chosen) AddFolder(folder);
        }
        if (!quiet)
            NotifyBriefly(chosen.Count == 0 ? "No new folders in the history" : chosen.Count == 1 ? "Added 1 folder" : $"Added {chosen.Count} folders",
                chosen.Count == 0
                    ? $"A folder is offered once it has {AutoPins.MinConversations} conversations in {AutoPins.Window.Days} days, and once only: a folder removed stays removed."
                    : string.Join(", ", chosen.Select(Paths.Leaf)) + ", each a workspace of its name. A folder removed stays removed.");
        return chosen;
    }

    /// <summary>
    /// The first run of folders from the history, once, after the first history refresh that succeeds on this PC (so after
    /// install, or after the update that brought it). It says what it added only when it added something. Not in the smoke or a
    /// restore check, whose fixtures decide their own folders.
    /// </summary>
    private async Task FirstSuggestionsAsync()
    {
        if (_preferences.AutoPinsDone || _options.SmokeTest || _options.RestoreOnly) return;
        _preferences.AutoPinsDone = true;
        SavePreferences();
        var chosen = await AddFoldersFromHistoryAsync(quiet: true);
        if (chosen.Count > 0)
            NotifyBriefly(chosen.Count == 1 ? "1 folder from your history" : $"{chosen.Count} folders from your history",
                string.Join(", ", chosen.Select(Paths.Leaf)) + ".");
    }

    /// <summary>
    /// Reads every open folder tree again, quietly: as the Folders panel comes into view, and as SIC's window comes back to the
    /// front with it showing, which is when a folder made elsewhere (in File Explorer, or by a pane's own mkdir) is looked for.
    /// Each shown tree's folder is read, then its open rows, one listing each.
    /// </summary>
    private void RefreshOpenFolders()
    {
        if (!_panelOpen || _preferences.ActivePanel != "folders") return;
        foreach (var view in _folderRows.Values.Where(view => view.Tree.Visibility == Visibility.Visible).ToArray()) _ = RelistRootAsync(view, quiet: true);
    }
}
