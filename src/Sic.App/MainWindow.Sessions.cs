using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.Alerts;
using Sic.Core.Appearance;
using Sic.Core.Sessions;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;

namespace Sic.App;

/// <summary>Runtime state of one tab: its saved descriptor, its panes in the order of its split tree, the canvas that tiles them, and the header widgets that show status.</summary>
internal sealed class TabHost
{
    internal required TabViewItem Item { get; init; }
    /// <summary>The tab as it would be saved now: its panes in tree order and its tree, kept current on every split, close and divider drag.</summary>
    internal required SavedTab Saved { get; set; }
    internal List<SessionPane> Panes { get; } = [];
    internal required PaneCanvas Content { get; init; }
    internal required Grid Header { get; init; }
    internal required TextBlock Title { get; init; }
    internal required Ellipse Dot { get; init; }
    /// <summary>Takes the dot's place while an agent in the tab works.</summary>
    internal required ProgressRing Ring { get; init; }
    internal required Border TileHost { get; init; }
    /// <summary>The emoji of the pinned folder the first pane works in, before the title; collapsed without one (docs/decisions/folder-emoji.md).</summary>
    internal required TextBlock Mark { get; init; }
    internal TabPaint Paint { get; } = new();
    /// <summary>The in-place rename box while a rename is open.</summary>
    internal TextBox? Editor { get; set; }
    /// <summary>The tab's state from its panes' trackers: the most urgent of them (<see cref="PaneState"/> rises in urgency).</summary>
    internal PaneState Presence { get; set; }
    /// <summary>Set once a close has been decided and its agents are being asked to exit; the layout no longer saves the tab.</summary>
    internal bool Closing { get; set; }
}

/// <summary>Where a new session goes when it splits a pane: the tab, the pane, and which way.</summary>
internal sealed record SplitTarget(TabHost Host, SessionPane Pane, PaneSplit Direction);

internal sealed partial class MainWindow
{
    /// <summary>
    /// Opens a tab for <paramref name="descriptor"/>, at the strip's end or at <paramref name="at"/>. A pane in <paramref name="reuse"/>
    /// is taken as it is, its terminal included, rather than made afresh: a pane moved out of another tab (<see cref="MovePaneToNewTab"/>).
    /// </summary>
    private TabHost CreateTab(SavedTab descriptor, bool select = true, int? at = null, IReadOnlyDictionary<string, SessionPane>? reuse = null)
    {
        // The tree the tab tiles by: the saved one, or one derived for a tab saved before there were trees. The panes are
        // listed in its order from here on, and the ratios of the old grid are not written again.
        var tree = PaneLayout.TreeOf(descriptor);
        // Whether the tab keeps its panes even: as saved, or, for a tab saved before there was the choice, when none of its
        // dividers has been dragged (docs/decisions/keep-panes-even.md). One that does is evened now: its halves become thirds.
        var keepEven = descriptor.KeepEven ?? PaneLayout.Untouched(tree);
        if (keepEven) tree = PaneLayout.Even(tree);
        var byId = descriptor.Panes.ToDictionary(pane => pane.Id, StringComparer.Ordinal);
        descriptor = descriptor with { Panes = PaneLayout.Leaves(tree).Select(id => byId[id]).ToList(), Layout = tree, KeepEven = keepEven, SplitRatio = null, RowRatio = null };
        var display = DisplayTitle(descriptor);
        // A transparent fill makes the whole header, not just the glyphs, a double-click target.
        // The tile, the folder emoji, the title, and the dot or the ring. The row overhangs the tab's content area by 4 px at the
        // bottom, so the tab is 28 px (TabHeight) round a 24 px row, with the row 4 px clear of the color stripe at the top.
        var header = new Grid { ColumnSpacing = 8, Height = TabContentHeight, Margin = new Thickness(0, 0, 0, -TabContentOverhang), Background = _theme.Transparent };
        // The tile, the folder emoji, the title, the pin while the tab is pinned, and the dot or the ring.
        for (var column = 0; column < 5; column++) header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tileHost = new Border { VerticalAlignment = VerticalAlignment.Center, Child = _theme.AgentTile(descriptor.Panes[0].AgentId, 16) };
        header.Children.Add(tileHost);
        // A collapsed column takes no spacing, so a tab without an emoji is laid out as before (RefreshTabMark).
        var mark = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        Grid.SetColumn(mark, 1); header.Children.Add(mark);
        var title = _theme.BodyText(ShortTitle(TabLabel(descriptor)), 13); title.MaxWidth = 200;
        // Inherit the ink the tab's visual states set (see PaintTab); a fixed brush ignored selection and the rail color.
        title.ClearValue(TextBlock.ForegroundProperty);
        Grid.SetColumn(title, 2); header.Children.Add(title);
        var dot = Theme.Dot(_theme.Neutral, 6); dot.Visibility = Visibility.Collapsed;
        Grid.SetColumn(dot, 4); header.Children.Add(dot);
        var ring = new ProgressRing
        {
            Width = 12, Height = 12, MinWidth = 0, MinHeight = 0, IsIndeterminate = true, IsActive = false, Foreground = _theme.Accent,
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(ring, 4); header.Children.Add(ring);
        // The template's 32 px minimum would hold the tab above its content.
        var tab = new TabViewItem { Header = header, Tag = descriptor, IsClosable = true, MinHeight = 0 };
        ToolTipService.SetToolTip(tab, display + "\n" + descriptor.Panes[0].WorkingDirectory);
        var content = new PaneCanvas(_theme, tree);
        var host = new TabHost { Item = tab, Saved = descriptor, Content = content, Header = header, Title = title, Dot = dot, Ring = ring, TileHost = tileHost, Mark = mark };
        // A dragged divider is the tab's own tiling from then on: a tab that kept its panes even stops (docs/decisions/keep-panes-even.md).
        content.RatioChanged += (_, _) =>
        {
            host.Saved = host.Saved with { Layout = content.Tree, KeepEven = false };
            host.Item.Tag = host.Saved;
            foreach (var pane in host.Panes) pane.KeepEven = false;
            SaveLayout();
        };
        WatchPaneDrops(content);
        host.Paint.Attach(tab);
        tab.Loaded += (_, _) => { AttachStripe(host); FitCloseButton(tab); TrimTabFeet(host); MirrorTab(host); };
        header.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler((_, args) =>
        {
            if (host.Editor is not null) return;
            args.Handled = true;
            BeginRename(host);
        }), handledEventsToo: true);
        foreach (var saved in descriptor.Panes)
        {
            if (reuse is not null && reuse.TryGetValue(saved.Id, out var kept)) { kept.Rechrome(host.Paint); host.Panes.Add(kept); }
            else host.Panes.Add(CreatePane(host, saved));
        }
        LayoutPanes(host);
        var menu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = "Rename tab", KeyboardAcceleratorTextOverride = "Ctrl+F2", Icon = new FontIcon { Glyph = "\uE8AC", FontFamily = Theme.Icons } };
        rename.Click += (_, _) => BeginRename(host);
        var color = new MenuFlyoutItem { Text = "Tab color…", Icon = new FontIcon { Glyph = "\uE790", FontFamily = Theme.Icons } };
        // Opened after the menu has closed, so the two light-dismiss popups do not fight.
        color.Click += (_, _) => DispatcherQueue.TryEnqueue(() => ShowColorFlyout(host));
        // The splits act on the tab's focused pane, the one a shortcut would split.
        MenuFlyoutItem SplitItem(PaneSplit direction)
        {
            var item = Theme.SplitMenuItem(direction);
            item.Click += async (_, _) => await SplitAsync(host, FocusedPane(host), direction);
            menu.Opening += (_, _) => item.IsEnabled = host.Panes.Count < PaneLayout.MaxPanes && FocusedPane(host).FitsSplit(direction);
            return item;
        }
        var splitRight = SplitItem(PaneSplit.Columns);
        var splitDown = SplitItem(PaneSplit.Rows);
        var evenItem = new ToggleMenuFlyoutItem { Text = "Keep panes even" };
        ToolTipService.SetToolTip(evenItem, Theme.KeepEvenTip);
        evenItem.Click += (_, _) => SetKeepEven(host, evenItem.IsChecked);
        menu.Opening += (_, _) => evenItem.IsChecked = host.Saved.KeepEven == true;
        var duplicate = new MenuFlyoutItem { Text = "Duplicate tab", KeyboardAcceleratorTextOverride = "Ctrl+Shift+D", Icon = new FontIcon { Glyph = "\uE8C8", FontFamily = Theme.Icons } };
        duplicate.Click += async (_, _) => await DuplicateAsync(FocusedPane(host));
        // The focused pane out to a tab of its own, after this one; greyed in a tab of one pane, which already is one.
        var moveOut = new MenuFlyoutItem { Text = "Move pane to new tab", Icon = new FontIcon { Glyph = "\uE8A7", FontFamily = Theme.Icons } };
        moveOut.Click += (_, _) => { try { MovePaneToNewTab(FocusedPane(host)); } catch (Exception exception) { ShowError(exception.Message); } };
        menu.Opening += (_, _) => moveOut.IsEnabled = host.Panes.Count > 1;
        var resumeAll = new MenuFlyoutItem { Text = "Resume all tabs", KeyboardAcceleratorTextOverride = "F5", Icon = ResumeAllMark.Icon() };
        resumeAll.Click += async (_, _) => await ResumeAllFromUserAsync(new("Resume all tabs on the tab's right-click menu", DateTimeOffset.UtcNow));
        menu.Opening += (_, _) => resumeAll.IsEnabled = !ResumingAll && AllPanes.Any(pane => !pane.IsRunning);
        // The tab's own workspace, read as the menu opens and as it is clicked: the tab may have moved to another meanwhile.
        var resumeHere = new MenuFlyoutItem { Text = "Resume workspace tabs", KeyboardAcceleratorTextOverride = "Ctrl+F5", Icon = ResumeAllMark.Icon() };
        resumeHere.Click += async (_, _) => await ResumeWorkspaceFromUserAsync(WorkspaceOf(host), new("Resume workspace tabs on the tab's right-click menu", DateTimeOffset.UtcNow));
        menu.Opening += (_, _) => resumeHere.IsEnabled = !ResumingAll && WorkspaceOf(host) is { } own && HasResumable(own);
        var consolidate = new MenuFlyoutItem { Text = "Consolidate tabs by folder", KeyboardAcceleratorTextOverride = "F6", Icon = new FontIcon { Glyph = ConsolidateGlyph, FontFamily = Theme.Icons } };
        consolidate.Click += (_, _) => ConsolidateTabsFromUser();
        menu.Opening += (_, _) => consolidate.IsEnabled = ConsolidationGroups().Count > 0;
        // The other tabs of this workspace; another workspace's tabs are out of view and out of reach here.
        var closeOthers = new MenuFlyoutItem { Text = "Close other tabs" };
        closeOthers.Click += async (_, _) => await CloseTabsAsync(_tabs.TabItems.OfType<TabViewItem>().Where(item => item != tab && _open.ContainsKey(item)).ToArray());
        // Pin: the tab goes to the top of Sessions, so a tab closed to make room in the strip is a click away: a split tab whole, as
        // a group (MainWindow.Groups.cs), a tab of one pane through its conversation. Pin tab keeps the tab open and reads Unpin tab
        // while the tab is pinned; Pin and close tab closes it too. Both are greyed for a tab of one pane with nothing saved to pin
        // (a shell, an agent pane not yet linked, a new session nothing was typed into).
        var pinTab = new MenuFlyoutItem { Text = "Pin tab", Icon = new FontIcon { Glyph = PinGlyph, FontFamily = Theme.Icons } };
        pinTab.Click += async (_, _) => await TogglePinTabAsync(tab);
        var pinClose = new MenuFlyoutItem { Text = "Pin and close tab", Icon = new FontIcon { Glyph = PinGlyph, FontFamily = Theme.Icons } };
        pinClose.Click += async (_, _) => await PinAndCloseTabAsync(tab);
        menu.Opening += (_, _) =>
        {
            var canPin = CanPinTab(host);
            var pinned = canPin && IsTabPinned(host);
            pinTab.IsEnabled = pinClose.IsEnabled = canPin;
            pinTab.Text = pinned ? "Unpin tab" : "Pin tab";
            pinTab.Icon = new FontIcon { Glyph = pinned ? Theme.UnpinGlyph : PinGlyph, FontFamily = Theme.Icons };
        };
        var close = new MenuFlyoutItem { Text = "Close tab", Icon = new FontIcon { Glyph = "\uE711", FontFamily = Theme.Icons } };
        close.Click += async (_, _) => await CloseTabAsync(tab);
        menu.Items.Add(rename); menu.Items.Add(color); menu.Items.Add(splitRight); menu.Items.Add(splitDown); menu.Items.Add(evenItem); menu.Items.Add(moveOut); menu.Items.Add(duplicate); menu.Items.Add(resumeAll); menu.Items.Add(resumeHere); menu.Items.Add(consolidate); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(closeOthers); menu.Items.Add(pinTab); menu.Items.Add(pinClose); menu.Items.Add(close);
        tab.ContextFlyout = menu;
        tab.Content = content;
        _open.Add(tab, host);
        if (at is { } index && index >= 0 && index < _tabs.TabItems.Count) _tabs.TabItems.Insert(index, tab);
        else _tabs.TabItems.Add(tab);
        if (select) _tabs.SelectedItem = tab;
        PaintTab(host);
        RefreshTabStatus(host);
        // A restore brings the status bar up to date once, after its last tab (MainWindow's constructor).
        if (!_restoring) UpdateStatus();
        return host;
    }

    /// <summary>
    /// A program put text on the clipboard (OSC 52): Claude Code and Codex copy what is selected in their own screens this way.
    /// Just after the user's own key, click or drag in the pane an agent says "copied" itself, so nothing is added, and a
    /// PowerShell pane gets a brief notice. A copy nobody asked for, which anything a pane prints can make, gets a notice that
    /// stays, with the start of the text, so it is seen before it is pasted (<see cref="ClipboardText.NoticeForProgramCopy"/>).
    /// </summary>
    private void NoteProgramCopy(SessionPane source, string text, TimeSpan? sinceGesture)
    {
        switch (ClipboardText.NoticeForProgramCopy(source.Descriptor.AgentId == "shell", sinceGesture))
        {
            case ProgramCopyNotice.Brief: NotifyBriefly("Copied to the clipboard", Characters(text.Length) + " from " + CopyingProgram(source) + ": “" + ClipboardText.Preview(text) + "”."); break;
            case ProgramCopyNotice.Held:
                Notify(InfoBarSeverity.Warning, "A program changed the clipboard", CopyingProgram(source) + " copied " + Characters(text.Length) + " without a click or keypress: “" + ClipboardText.Preview(text) + "”.");
                break;
        }
    }

    private string CopyingProgram(SessionPane source) =>
        Theme.AgentName(source.Descriptor.AgentId) + (HostOf(source) is { } owner ? " in “" + (owner.Panes.Count > 1 ? source.Label : DisplayTitle(owner.Saved)) + "”" : "");

    private static string Characters(int count) => count.ToString("N0") + (count == 1 ? " character" : " characters");

    private SessionPane CreatePane(TabHost host, SavedPane saved)
    {
        var pane = new SessionPane(saved, Path.Combine(_options.DataDirectory, "WebView2"), _preferences, _theme, host.Paint,
            started => ResumePaneAsync(started, new(started.Descriptor.AgentId == "shell" ? "the pane's Start terminal button" : "the pane's Resume conversation button", DateTimeOffset.UtcNow)),
            LinkPaneAsync, RenamePaneAsync, static _ => null, ConfirmPasteAsync, candidate => PinnableKey(candidate) is not null) { HeaderAtBottom = TabsBottom, RingWidth = _preferences.PaneBorderWidth, RingColor = PaneBorderColor };
        // A click in a pane lets go of a folder chosen in the Folders panel: the next New session starts in this pane's folder.
        pane.Selected += selected => { ForgetSelectedFolder(); SetActivePane(selected); };
        pane.PinRequested += async source => await TogglePinPaneAsync(source);
        pane.PinAndCloseRequested += async source => { if (HostOf(source) is { } owner) await PinAndClosePaneAsync(owner, source); };
        pane.TurnOnAgentRequested += id => SetAgentEnabled(id, true);
        pane.StartNewHereRequested += async source => await StartNewHereAsync(source);
        // The pane's tab is looked up at each event rather than kept from here: a pane moves to another tab when its tab is
        // dropped into one (MoveTabInto), and the tab it was made in is gone by then.
        pane.StatusChanged += () => { if (HostOf(pane) is { } owner) RefreshTabStatus(owner); UpdateStatus(); if (pane.IsRunning) WatchPresence(); };
        pane.Presence.Selected(ReferenceEquals(_tabs.SelectedItem, host.Item) && WindowInView, DateTimeOffset.UtcNow);
        pane.PresenceChanged += _ => { if (HostOf(pane) is { } owner) RefreshPresence(owner); };
        pane.SplitRequested += async (source, direction) => await SplitAsync(HostOf(source), source, direction);
        pane.KeepEvenRequested += (source, on) => { if (HostOf(source) is { } home) SetKeepEven(home, on); };
        pane.DuplicateRequested += async source => await DuplicateAsync(source);
        pane.MoveOutRequested += source => { try { MovePaneToNewTab(source); } catch (Exception exception) { ShowError(exception.Message); } };
        pane.MoveRequested += BeginPaneDrag;
        pane.CloseRequested += async closing => { if (HostOf(closing) is { } owner) await ClosePaneAsync(owner, closing); };
        pane.Shortcut += async name => await HandleShortcutAsync(name);
        pane.ProgramCopied += NoteProgramCopy;
        pane.ProgramCopyRefused += (source, characters) => Notify(InfoBarSeverity.Warning, "Not copied",
            CopyingProgram(source) + " tried to copy " + Characters(characters) + ", over the 1 MB limit. Shift+drag selects the text for copying instead.");
        // The placeholder page, once: with the saved conversation when the pane has one, else the page for a shell or an unlinked pane.
        if (saved.SessionId is not null) ShowSavedPreview(pane, TabLabels.Key(saved.AgentId, saved.SessionId));
        else pane.ShowPreview(null);
        return pane;
    }

    /// <summary>The records a launch's restore read in one query, for the panes it makes (<see cref="ShowSavedPreview"/>); null outside a restore.</summary>
    private Dictionary<string, Conversation>? _restoredRecords;

    /// <summary>
    /// The placeholder for a saved conversation: its record read here, which is small (from the restore's one query for every
    /// restored pane, else one of its own), and the part of its transcript the page shows, read off the UI thread and filled in
    /// when it arrives. Only that part leaves the catalog (one character past the page's limit, which tells the page the text
    /// was cut); the whole transcript, which can run to megabytes, used to be read for every restored pane. Restoring a layout
    /// of many panes no longer reads every transcript first.
    /// </summary>
    private void ShowSavedPreview(SessionPane pane, string key)
    {
        var record = _restoredRecords is { } restored ? restored.GetValueOrDefault(key) : _catalog.GetHeader(key);
        pane.ShowPreview(record, loadBody: () => Task.Run(() => _catalog.GetBodyStart(key, SessionPane.TranscriptPreviewLimit + 1)?.Text));
    }

    /// <summary>
    /// Tiles the tab's panes as its split tree says (<see cref="PaneLayout"/>), with a draggable divider at every split. The
    /// canvas keeps every pane a direct child and only arranges them, so a live terminal is moved rather than unloaded.
    /// </summary>
    private void LayoutPanes(TabHost host)
    {
        host.Content.Show(host.Saved.Layout!, host.Panes);
        foreach (var pane in host.Panes)
        {
            pane.CanSplit = host.Panes.Count < PaneLayout.MaxPanes;
            pane.CanMoveOut = host.Panes.Count > 1;
            pane.KeepEven = host.Saved.KeepEven == true;
            // They change only with the tree.
            pane.Neighbors = AltArrowSides(host.Saved.Layout!, pane.Descriptor.Id);
        }
    }

    /// <summary>
    /// The sides a pane's terminal takes Alt+Arrow toward: those another pane lies on, and always the strip's side, since the tab
    /// strip lies beyond the top row, or beyond the bottom row with the tabs at the bottom (<see cref="StripSide"/>).
    /// </summary>
    private PaneNeighbors AltArrowSides(PaneNode tree, string pane) =>
        TabsBottom ? PaneLayout.NeighborsOf(tree, pane) with { Down = true } : PaneLayout.NeighborsOf(tree, pane) with { Up = true };

    /// <summary>
    /// Adds a pane to a tab that has room for one, splitting <paramref name="after"/>'s room with it (to its right for columns,
    /// below it for rows), and retiles the tab. The new pane follows the one it split in the tab's order.
    /// </summary>
    private SessionPane AddPane(TabHost host, SavedPane saved, SessionPane after, PaneSplit direction)
    {
        if (host.Panes.Count >= PaneLayout.MaxPanes) throw new InvalidOperationException($"A tab holds up to {PaneLayout.MaxPanes} panes.");
        if (!host.Panes.Contains(after)) throw new InvalidOperationException("The pane to split is no longer in the tab.");
        var tree = PaneLayout.Split(host.Content.Tree, after.Descriptor.Id, direction, saved.Id);
        var pane = CreatePane(host, saved);
        host.Panes.Insert(host.Panes.IndexOf(after) + 1, pane);
        SetTree(host, tree);
        RefreshTabStatus(host);
        return pane;
    }

    /// <summary>
    /// Gives the tab a new tree, lists its panes in the tree's order, and retiles it. A tab that keeps its panes even is given
    /// the tree evened (<see cref="PaneLayout.Even"/>): every split, close, move and drop goes through here, and a divider drag does not.
    /// </summary>
    private void SetTree(TabHost host, PaneNode tree)
    {
        if (host.Saved.KeepEven == true) tree = PaneLayout.Even(tree);
        var order = PaneLayout.Leaves(tree).Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        host.Panes.Sort((left, right) => order[left.Descriptor.Id].CompareTo(order[right.Descriptor.Id]));
        host.Saved = host.Saved with { Panes = host.Panes.Select(item => item.Descriptor).ToList(), Layout = tree };
        host.Item.Tag = host.Saved;
        LayoutPanes(host);
    }

    /// <summary>
    /// Turns keeping the tab's panes even on or off, from the tab's menu or a pane's (docs/decisions/keep-panes-even.md). On,
    /// the tab is evened now and after every split and close, until a divider is dragged; off, its ratios stay as they are.
    /// </summary>
    private void SetKeepEven(TabHost host, bool on)
    {
        host.Saved = host.Saved with { KeepEven = on };
        host.Item.Tag = host.Saved;
        SetTree(host, host.Content.Tree);
        SaveLayout();
    }

    /// <summary>The tab's tree, a divider drag under way included, less the panes whose close is decided.</summary>
    private static PaneNode TreeWithout(TabHost host, IReadOnlyCollection<SessionPane> kept)
    {
        var tree = host.Content.Tree;
        foreach (var pane in host.Panes.Where(pane => !kept.Contains(pane))) tree = PaneLayout.Remove(tree, pane.Descriptor.Id)!;
        return tree;
    }

    private SessionPane? FindPane(Conversation conversation) =>
        AllPanes.FirstOrDefault(pane => pane.Descriptor.AgentId == conversation.AgentId && pane.Descriptor.SessionId == conversation.SessionId);

    private TabHost? HostOf(SessionPane pane) => _open.Values.FirstOrDefault(host => host.Panes.Contains(pane));

    /// <summary>
    /// Starts a new session of the pane's agent in the pane's folder, in a tab of its own. Nothing is resumed and neither the
    /// name nor the color is copied: it is a different conversation that happens to share the folder.
    /// </summary>
    private async Task DuplicateAsync(SessionPane source)
    {
        try { await StartNewAsync(source.Descriptor.AgentId, source.Descriptor.WorkingDirectory); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    /// <summary>
    /// The tab's focused pane: the one with keyboard focus when it is in that tab, else the one focused last in it (its first,
    /// when none has been). What a duplicate copies, a shortcut splits, Alt+Arrow moves from, and a tab shown again focuses.
    /// </summary>
    private SessionPane FocusedPane(TabHost host) => _activePane is { } active && host.Panes.Contains(active) ? active : host.Panes.MaxBy(pane => pane.LastActive)!;

    /// <summary>The way the pane splits when none is named: along its longer side, or the other way when only that has the room; null when neither does.</summary>
    private static PaneSplit? SplitThatFits(SessionPane pane) => PaneLayout.SplitThatFits(pane.ActualWidth, pane.ActualHeight, PaneSplitter.GrabSize);

    /// <summary>
    /// Splits <paramref name="source"/> with a new session, to its right (columns) or below it (rows): the New session dialog,
    /// then a pane in the room the source gives up. Refused briefly when the tab is full or the pane has not the room that way.
    /// </summary>
    private async Task SplitAsync(TabHost? host, SessionPane? source, PaneSplit direction)
    {
        if (host is null || source is null) { await ShowNewSessionAsync(); return; }
        if (host.Panes.Count >= PaneLayout.MaxPanes) { NotifyBriefly("This tab is full", $"A tab holds up to {PaneLayout.MaxPanes} panes."); return; }
        if (!source.FitsSplit(direction))
        {
            var columns = direction == PaneSplit.Columns;
            NotifyBriefly(columns ? "This pane is too narrow to split" : "This pane is too short to split",
                (columns ? "Two side-by-side panes need " : "Two stacked panes need ") + PaneLayout.LeastToSplit(direction, PaneSplitter.GrabSize) + " px.");
            return;
        }
        await ShowNewSessionAsync(source.Descriptor.WorkingDirectory, new SplitTarget(host, source, direction));
    }

    /// <summary>The tab's name: its own, or its first pane's (<see cref="TabLabels.TabTitle"/>).</summary>
    private string DisplayTitle(SavedTab descriptor) => TabLabels.TabTitle(descriptor, _preferences.ConversationNames);

    /// <summary>The title the tab itself shows: no harness name, because the agent tile beside it says that. The tooltip keeps the full title.</summary>
    private string TabLabel(SavedTab descriptor) => TabLabels.WithoutAgentName(DisplayTitle(descriptor));

    private static string ShortTitle(string title) => title.Length > 28 ? title[..27] + "…" : title;

    /// <summary>
    /// An interactive PowerShell; under the smoke test, the fixture script instead, so a verification run never loads the
    /// user's profile and a shell it starts through the ordinary paths answers the fixture's verbs.
    /// </summary>
    /// <summary>A PowerShell tab's arguments: the PowerShell card's own, and the hook by which the tab reports its state (<see cref="Sic.Core.Shell.ShellStatus"/>).</summary>
    private IReadOnlyList<string> NewShellArguments => _options.SmokeTest ? SmokeShellArguments : Sic.Core.Shell.ShellStatus.Arguments(_preferences.ShellArguments);

    private static TerminalLaunchRequest CreateShellRequest(IReadOnlyList<string> arguments, string directory) => new(AgentDetection.PowerShellPath, arguments, directory,
        Environment: new Dictionary<string, string?> { ["TERM"] = "xterm-256color", ["COLORTERM"] = "truecolor", ["TERM_PROGRAM"] = "SIC", ["TERMINAL_CWD"] = directory });
}
