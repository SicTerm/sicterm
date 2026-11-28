using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.Appearance;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Sic.App;

/// <summary>
/// A workspace as the window holds it: the group of tabs of one folder in the Folders list, or Scratch for the tabs outside every
/// listed folder, with a strip of its own (docs/decisions/workspaces.md). The current workspace's tabs are the tab view's items;
/// every other workspace keeps its tabs in <see cref="Parked"/>, in strip order, their panes and terminals alive and out of view
/// as an unselected tab's are. <see cref="Id"/> is the folder as the list keeps it, or <see cref="Workspaces.ScratchId"/>;
/// <see cref="Home"/> is the folder, null for Scratch. <see cref="SelectedTabId"/> is the tab the strip shows when the workspace
/// is switched to, kept while it is parked.
/// </summary>
internal sealed class Workspace
{
    internal required string Id { get; init; }
    internal string? Home { get; init; }
    internal List<TabViewItem> Parked { get; } = [];
    internal string? SelectedTabId { get; set; }
    internal bool IsScratch => Home is null;
}

/// <summary>
/// Workspaces: groups of tabs, each with a tab strip of its own, one per folder in the Folders list and Scratch for the rest
/// (docs/decisions/workspaces.md). Since November 8, 2026 the folder decides: a tab belongs to the workspace of the deepest
/// listed folder holding its first pane's working folder (<see cref="Workspaces.RootFor"/>), a new tab is placed there as it
/// opens (<see cref="PlaceTab"/>), the tabs regroup when a folder is added or removed, and a layout read back regroups by the
/// same rule, so nothing about a workspace is made, named or moved by hand. Switching swaps the strip whole (<see cref="SwitchWorkspace"/>);
/// the Workspaces panel lists them, each open to its sessions until its chevron folds it to a row with a second line; the
/// indicator at the head of the strip names the current one and lists them all on a right-click.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The rail's glyph for the Agent Workspaces panel (Workspaces, then Agents, until November 9, 2026): a robot (E99A), for the agents open in
    /// each workspace; four tiles (E8A9) until Chris chose it from a page of candidates that day.</summary>
    internal const string WorkspaceGlyph = "\uE99A";

    /// <summary>The rail's glyph for Sessions, and the empty window's Open Sessions: a magnifier (E721), since the panel searches every
    /// conversation; a three-line list (EA37) until November 9, 2026.</summary>
    internal const string SearchGlyph = "\uE721";
    /// <summary>The rail's glyph for Folders, and a workspace's while its folder has no emoji.</summary>
    internal const string FolderGlyph = "";
    /// <summary>Scratch's glyph: a note, for the sessions outside every listed folder.</summary>
    internal const string ScratchGlyph = "";
    private readonly List<Workspace> _workspaces = [];
    private Workspace _workspace = null!;
    // Set while the strip's items are swapped for another workspace's, so the selection changes on the way are not acted on.
    private bool _switchingWorkspace;
    // Set while a tab is between workspaces (MoveTabToWorkspace): a Scratch just made for it is empty until the tab lands, and must not be trimmed on the way.
    private bool _movingTab;
    private StackPanel _workspaceList = null!;
    private readonly Dictionary<string, WorkspaceRowView> _workspaceRows = new(StringComparer.OrdinalIgnoreCase);
    private string _workspacePanelSignature = "";
    private Border _workspaceBadge = null!;
    private Button _workspaceChip = null!;
    private TextBlock _workspaceChipText = null!;
    private TextBlock _workspaceChipMark = null!;
    private FontIcon _workspaceChipGlyph = null!;
    /// <summary>The indicator's live brushes, recolored for the workspace's folder color and the theme (<see cref="RefreshWorkspaceChip"/>).</summary>
    private readonly SolidColorBrush _chipFill = new(Microsoft.UI.Colors.Transparent), _chipHover = new(Microsoft.UI.Colors.Transparent),
        _chipPressed = new(Microsoft.UI.Colors.Transparent), _chipInk = new(Microsoft.UI.Colors.Transparent);
    // How strongly a folder's color washes the tab strip, and the indicator on it under the pointer and pressed.
    private const double StripWash = 0.4, ChipHoverWash = 0.52, ChipPressedWash = 0.46;
    /// <summary>The strip's surface while the workspace has a color (<see cref="PaintWorkspaceChip"/>); the theme's rail brush otherwise.</summary>
    private readonly SolidColorBrush _stripTint = new(Microsoft.UI.Colors.Transparent);
    /// <summary>Set by the strip's layout (FitTabList) while the strip has no room for the indicator and <see cref="MinTabRoom"/> of tabs; and the indicator's last measured width.</summary>
    private bool _chipCramped;
    private double _chipWidth = 110;
    private const double MinTabRoom = 120;

    /// <summary>Verification: the workspaces in the panel's order, and the current one.</summary>
    internal IReadOnlyList<Workspace> WorkspaceList => _workspaces;
    internal Workspace CurrentWorkspace => _workspace;

    /// <summary>
    /// The Folders list: the folders you work in, in the panel's order, each a workspace's root; what the pinned folders and the
    /// Workplaces list were until November 8, 2026 (<see cref="AppPreferences.Folders"/>). Cleaned on load, so it is read as it is.
    /// </summary>
    internal List<string> Folders => _preferences.Folders ?? [];

    /// <summary>Whether a folder is in the list, however it is written.</summary>
    internal bool IsFolder(string? folder) => folder is not null && PinnedFolders.Contains(Folders, folder);

    /// <summary>The listed folder a working folder belongs to, the deepest that holds it (<see cref="Workspaces.RootFor"/>); null outside them all.</summary>
    internal string? RootFor(string? folder) => Workspaces.RootFor(Folders, folder);

    /// <summary>The workspace of a listed folder, however it is written; null for a folder that is not listed.</summary>
    internal Workspace? WorkspaceOfFolder(string? folder) => folder is null ? null : _workspaces.FirstOrDefault(workspace => workspace.Home is not null && PinnedFolders.Same(workspace.Home, folder));

    /// <summary>The workspace a session in <paramref name="folder"/> belongs to: its listed folder's, else Scratch, made if there is none.</summary>
    internal Workspace WorkspaceFor(string? folder) => WorkspaceOfFolder(RootFor(folder)) ?? EnsureScratch();

    private Workspace? Scratch => _workspaces.FirstOrDefault(workspace => workspace.IsScratch);

    /// <summary>Scratch, made at the end of the list when there is none: for a tab that belongs to no listed folder.</summary>
    private Workspace EnsureScratch()
    {
        if (Scratch is { } scratch) return scratch;
        var made = new Workspace { Id = Workspaces.ScratchId };
        _workspaces.Add(made);
        return made;
    }

    /// <summary>Scratch goes once it holds no tab, is not the workspace showing, and there is a listed folder to stand in its place.</summary>
    private void TrimScratch()
    {
        if (_movingTab || Scratch is not { } scratch || ReferenceEquals(scratch, _workspace) || Folders.Count == 0 || ItemsOf(scratch).Any(_open.ContainsKey)) return;
        _workspaces.Remove(scratch);
    }

    /// <summary>The tabs of a workspace, in strip order: the tab view's items for the current one, the parked list for any other.</summary>
    private IEnumerable<TabViewItem> ItemsOf(Workspace workspace) => ReferenceEquals(workspace, _workspace) ? _tabs.TabItems.OfType<TabViewItem>() : workspace.Parked;

    /// <summary>The open tabs of a workspace, in strip order.</summary>
    internal IEnumerable<TabHost> HostsOf(Workspace workspace) => ItemsOf(workspace).Where(_open.ContainsKey).Select(item => _open[item]);

    /// <summary>The open tabs the strip shows, in its order: the current workspace's.</summary>
    private IEnumerable<TabHost> StripHosts => _tabs.TabItems.OfType<TabViewItem>().Where(_open.ContainsKey).Select(item => _open[item]);

    /// <summary>
    /// Whether a workspace's row shows its sessions: every row does until its chevron folds it, and nothing else folds or opens
    /// one, a switch least of all (asked for by Chris on November 8, 2026: "Workspaces should only be expanded or closed if the
    /// arrow is clicked"). The fold is saved by key (<see cref="SectionFolds.Tabs"/>), so a row folded stays folded at the next launch.
    /// </summary>
    internal bool WorkspaceExpanded(Workspace workspace) => !_preferences.FoldedSections.Contains(SectionFolds.Tabs(workspace.Id));

    /// <summary>The chevron's click: folds the workspace's sessions under its row, or shows them again; saved.</summary>
    internal void ToggleWorkspaceFold(Workspace workspace)
    {
        _preferences.FoldedSections = SectionFolds.With(_preferences.FoldedSections, SectionFolds.Tabs(workspace.Id), WorkspaceExpanded(workspace));
        SavePreferences();
        RefreshWorkspacePanel();
    }

    /// <summary>The workspace a tab is in, by its item; null for a tab that is in none (gone).</summary>
    internal Workspace? WorkspaceOf(TabHost host) => _workspaces.FirstOrDefault(workspace => ItemsOf(workspace).Contains(host.Item));

    /// <summary>The workspace as its row and its name read it: its tabs as they stand, without the closing ones.</summary>
    private SavedWorkspace Describe(Workspace workspace) => new(workspace.Id, HostsOf(workspace).Where(host => !host.Closing).Select(host => host.Saved).ToList(),
        Home: workspace.Home, SelectedTabId: ReferenceEquals(workspace, _workspace) ? SelectedHost?.Saved.Id : workspace.SelectedTabId);

    /// <summary>What the workspace is called: its folder's own name, or Scratch (<see cref="Workspaces.Name"/>).</summary>
    internal string WorkspaceName(Workspace workspace) => Workspaces.Name(Describe(workspace));

    /// <summary>The workspace as layout.json saves it: each tab as it would be saved now, less the tabs and panes whose close is decided.</summary>
    private SavedWorkspace SavedWorkspaceOf(Workspace workspace) => new(workspace.Id,
        HostsOf(workspace).Where(open => !open.Closing && open.Panes.Any(pane => !pane.Closing)).Select(SavedNow).ToList(),
        Home: workspace.Home, SelectedTabId: ReferenceEquals(workspace, _workspace) ? SelectedHost?.Saved.Id : workspace.SelectedTabId);

    /// <summary>
    /// The open workspaces, tabs and panes as layout.json records them; what <see cref="SaveLayout"/> writes when its quiet period
    /// runs out. The current workspace's tabs are written as Tabs too, so a build from before there were workspaces restores them.
    /// </summary>
    private WorkspaceLayout CurrentLayout()
    {
        var workspaces = _workspaces.Select(SavedWorkspaceOf).ToList();
        var current = workspaces.First(workspace => workspace.Id == _workspace.Id);
        return new WorkspaceLayout { SelectedWorkspaceId = _workspace.Id, Workspaces = workspaces, SelectedTabId = current.SelectedTabId, Tabs = current.Tabs };
    }

    /// <summary>Set while a launch restores its tabs: the status bar and the rail are brought up to date once at the end rather than tab by tab.</summary>
    private bool _restoring;

    /// <summary>
    /// Brings the saved layout back: the saved tabs regrouped by the rule into a workspace per listed folder and Scratch
    /// (<see cref="Workspaces.Regroup"/>; a settings file from before the list fills it from the saved homes first,
    /// <see cref="Workspaces.AdoptFolders"/>), each tab made as it was saved and the tabs of every workspace but the current one
    /// parked out of the strip, then the current workspace and its tab selected.
    /// </summary>
    private void RestoreLayout(WorkspaceLayout layout)
    {
        if (layout.Version != 1 || layout.Tabs is null || layout.Tabs.Count > Workspaces.MaxTabs)
            throw new InvalidDataException("Unsupported or oversized tab layout. It has not been changed.");
        var (read, readSelected) = Workspaces.Read(layout);
        var adopted = Workspaces.AdoptFolders(read, _preferences);
        if (adopted) SavePreferences();
        var (saved, selectedId) = Workspaces.Regroup(read, readSelected, Folders);
        foreach (var tab in saved.SelectMany(workspace => workspace.Tabs))
            if (tab.Panes is null || tab.Panes.Count is < 1 or > PaneLayout.MaxPanes) throw new InvalidDataException($"A saved tab must contain one to {PaneLayout.MaxPanes} panes.");
        // Every restored pane's record in one query, before the first frame, where each pane used to open the catalog for its own.
        var keys = saved.SelectMany(workspace => workspace.Tabs).SelectMany(tab => tab.Panes).Where(pane => pane.SessionId is not null)
            .Select(pane => TabLabels.Key(pane.AgentId, pane.SessionId!)).Distinct(StringComparer.Ordinal).ToList();
        // A restore starts from nothing: the launch's, and the verification's, which empties the strip and restores what it saved.
        _workspaces.Clear();
        try
        {
            _restoring = true; _switchingWorkspace = true;
            var records = _restoredRecords = keys.Count == 0 ? [] : _catalog.Find(keys).ToDictionary(record => record.Key, StringComparer.Ordinal);
            foreach (var record in saved)
            {
                var workspace = new Workspace { Id = record.Id, Home = record.Home, SelectedTabId = record.SelectedTabId };
                _workspaces.Add(workspace);
                var items = new List<TabViewItem>();
                foreach (var tab in record.Tabs)
                {
                    // A new session the history has saved since the layout was written is a saved conversation from the start, as the
                    // first refresh would find it (NoteSavedSessionsAsync): its pin answers at once and its page is built once.
                    var panes = tab.Panes.Select(pane => pane is { Unsaved: true, SessionId: { } id } && records.ContainsKey(TabLabels.Key(pane.AgentId, id)) ? pane with { Unsaved = false } : pane).ToList();
                    items.Add(CreateTab(tab with { Panes = panes }, select: false).Item);
                }
                if (record.Id == selectedId) continue;
                foreach (var item in items) _tabs.TabItems.Remove(item);
                workspace.Parked.AddRange(items);
            }
        }
        finally { _restoring = false; _switchingWorkspace = false; _restoredRecords = null; }
        _workspace = _workspaces.First(workspace => workspace.Id == selectedId);
        var selected = StripHosts.FirstOrDefault(host => host.Saved.Id == _workspace.SelectedTabId)?.Item;
        _tabs.SelectedItem = selected ?? _tabs.TabItems.OfType<TabViewItem>().FirstOrDefault();
        SetActivePane(SelectedPanes.FirstOrDefault());
        RefreshFolderMarks();
        RefreshFoldersPanel();
        RefreshWorkspacePanel();
        if (adopted) SaveLayout();
    }

    /// <summary>
    /// Shows another workspace: the strip's tabs are parked with the tab that was showing remembered, the other workspace's tabs
    /// take the strip in their order, and its remembered tab is selected. The panes are moved as elements and keep running, as a
    /// tab switch leaves an unselected tab's panes running. Nothing happens for the current workspace.
    /// </summary>
    internal void SwitchWorkspace(Workspace target, bool chosen = true)
    {
        if (ReferenceEquals(target, _workspace) || !_workspaces.Contains(target)) return;
        // A workspace switched to names the next New session's folder, over a folder chosen in the Folders panel before it; a
        // switch SicTerm makes on its own (Resume all going round the workspaces) leaves the choice alone.
        if (chosen) ForgetSelectedFolder();
        var leaving = _workspace;
        leaving.Parked.Clear();
        leaving.Parked.AddRange(_tabs.TabItems.OfType<TabViewItem>());
        leaving.SelectedTabId = SelectedHost?.Saved.Id;
        if (SelectedHost is { Editor: not null } renaming) _ = EndRenameAsync(renaming, commit: true, refocus: false);
        _switchingWorkspace = true;
        try
        {
            // One at a time from the end, the path a close takes; the selection changes on the way are not acted on.
            while (_tabs.TabItems.Count > 0) _tabs.TabItems.RemoveAt(_tabs.TabItems.Count - 1);
            _workspace = target;
            foreach (var item in target.Parked) _tabs.TabItems.Add(item);
            target.Parked.Clear();
            var selected = StripHosts.FirstOrDefault(host => host.Saved.Id == target.SelectedTabId)?.Item;
            _tabs.SelectedItem = selected ?? _tabs.TabItems.OfType<TabViewItem>().FirstOrDefault();
        }
        finally { _switchingWorkspace = false; }
        FadeInStrip();
        Diagnostics.Log("workspace: switched to “" + WorkspaceName(target) + "” from “" + WorkspaceName(leaving) + "”: strip " + _tabs.TabItems.Count
            + ", parked " + string.Join(", ", _workspaces.Select(workspace => WorkspaceName(workspace) + "=" + workspace.Parked.Count)));
        OnTabSelected();
        if (_tabs.TabItems.Count == 0) SetActivePane(null);
        RefreshWorkspacePanel();
        UpdateStatus();
    }

    /// <summary>How long the other workspace's tabs take to fade in: half of what WinUI's own entrance for a new tab takes.</summary>
    private static readonly TimeSpan StripFade = TimeSpan.FromMilliseconds(85);

    /// <summary>
    /// The tabs of the workspace switched to fade in together, at once. WinUI's own transitions for tabs added to the strip waited
    /// for the old tabs to go before the new ones began, a pause that made the switch feel slow (Chris, November 12, 2026), so they
    /// are set aside for the swap and given back once it is laid out: a tab opened, closed or moved still animates as WinUI has it.
    /// </summary>
    private void FadeInStrip()
    {
        if (_options.SmokeTest || FindNamed<ListView>(_tabs, "TabListView") is not { } list) return;
        if (list.ItemContainerTransitions is { Count: > 0 } transitions)
        {
            list.ItemContainerTransitions = null;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => list.ItemContainerTransitions ??= transitions);
        }
        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From = 0, To = 1, Duration = new Duration(StripFade) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, list);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        var motion = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        motion.Children.Add(fade);
        motion.Begin();
    }

    /// <summary>Shows the tab, switching to its workspace first when it is in another; the tab's focused pane takes the keyboard as a tab switch gives it.</summary>
    internal void ShowHost(TabHost host)
    {
        if (!_open.ContainsKey(host.Item)) return;
        if (WorkspaceOf(host) is { } workspace && !ReferenceEquals(workspace, _workspace)) SwitchWorkspace(workspace);
        if (_tabs.TabItems.Contains(host.Item)) _tabs.SelectedItem = host.Item;
    }

    /// <summary>Takes a tab's item out of wherever it is listed: the strip, or the parked list of the workspace it is in.</summary>
    private void RemoveTabItem(TabViewItem item)
    {
        if (_tabs.TabItems.Remove(item)) return;
        foreach (var workspace in _workspaces) if (workspace.Parked.Remove(item)) return;
        Diagnostics.Log("workspace: a tab's item was listed nowhere when it was taken out");
    }

    /// <summary>
    /// Puts a tab that has just opened in the strip where the rule says it belongs: the workspace of its first pane's folder
    /// (<see cref="WorkspaceFor"/>), the strip following it there, so a session started from a folder row or resumed from Sessions
    /// shows in its own workspace. A tab already in its workspace stays. Returns the workspace.
    /// </summary>
    internal Workspace PlaceTab(TabHost host, bool follow = true)
    {
        var target = WorkspaceFor(Workspaces.FolderOf(host.Saved));
        if (WorkspaceOf(host) is { } from && !ReferenceEquals(from, target)) MoveTabToWorkspace(host, target, follow: follow);
        return target;
    }

    /// <summary>
    /// Adds a folder to the Folders list, at the end: a workspace of its name appears before Scratch, and every open tab working in
    /// the folder or under it (and under no deeper listed folder) moves there from Scratch or from the folder above, the strip
    /// staying where it is. A folder already listed is returned as it is. Refused past <see cref="Workspaces.MaxWorkspaces"/>.
    /// </summary>
    internal Workspace AddFolder(string folder)
    {
        var key = Workspaces.NormalizeHome(folder) ?? throw new ArgumentException("A folder is needed.");
        if (WorkspaceOfFolder(key) is { } listed) return listed;
        if (Folders.Count >= Workspaces.MaxWorkspaces) throw new InvalidOperationException($"The limit is {Workspaces.MaxWorkspaces} folders.");
        _preferences.Folders = PinnedFolders.Clean(Folders.Append(key));
        SavePreferences();
        var workspace = new Workspace { Id = Workspaces.IdFor(key), Home = key };
        _workspaces.Insert(Scratch is { } scratch ? _workspaces.IndexOf(scratch) : _workspaces.Count, workspace);
        foreach (var host in _open.Values.Where(host => !host.Closing && ReferenceEquals(WorkspaceFor(Workspaces.FolderOf(host.Saved)), workspace) && !ReferenceEquals(WorkspaceOf(host), workspace)).ToArray())
            MoveTabToWorkspace(host, workspace);
        Diagnostics.Log("folders: added " + key + ", " + HostsOf(workspace).Count() + " tabs");
        FoldersChanged();
        return workspace;
    }

    /// <summary>
    /// Add folder… in the Folders panel: a folder chosen in Windows' folder picker joins the list. Opened over a focused terminal
    /// (its WebView2), the picker took the window down with a fail-fast (0xC0000409; Chris, November 9, 2026, from the indicator's list,
    /// which offered it then), so whatever opens it holds the focus itself, as the panel's button does.
    /// </summary>
    private async Task AddFolderFromPickerAsync()
    {
        try
        {
            if (await PickFolderAsync() is not { } folder) return;
            var was = IsFolder(folder);
            AddFolder(folder);
            NotifyBriefly(was ? "Already in Folders" : "Added to Folders", Paths.Leaf(folder));
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    /// <summary>
    /// Takes a folder out of the Folders list. Its tabs stay open and go where the rule now puts them, the folder above or Scratch,
    /// and the strip follows them when the folder's workspace was showing. Its fold, its tree and its workspace go; its emoji,
    /// color and sound stay in the file for the day it is listed again.
    /// </summary>
    internal void RemoveFolder(string folder)
    {
        if (WorkspaceOfFolder(folder) is not { } workspace) return;
        var name = WorkspaceName(workspace);
        _preferences.Folders = Folders.Where(listed => !PinnedFolders.Same(listed, folder)).ToList();
        _preferences.FoldedSections = SectionFolds.Prune(_preferences.FoldedSections, Folders);
        SavePreferences();
        var hosts = HostsOf(workspace).ToArray();
        var showing = ReferenceEquals(workspace, _workspace);
        foreach (var host in hosts) MoveTabToWorkspace(host, WorkspaceFor(Workspaces.FolderOf(host.Saved)));
        if (showing)
        {
            var next = hosts.Length > 0 ? WorkspaceOf(hosts[0]) : null;
            SwitchWorkspace(next ?? _workspaces.FirstOrDefault(other => !ReferenceEquals(other, workspace)) ?? EnsureScratch());
        }
        _workspaces.Remove(workspace);
        Diagnostics.Log("folders: removed " + folder + ", " + hosts.Length + " tabs regrouped");
        FoldersChanged();
        NotifyBriefly("Removed from Folders", name + (hosts.Length == 0 ? "" : hosts.Length == 1 ? ". Its tab stays open." : $". Its {hosts.Length} tabs stay open."));
    }

    /// <summary>Moves a folder up or down the list by <paramref name="offset"/> places, its workspace with it; Scratch stays last.</summary>
    internal void MoveFolder(string folder, int offset)
    {
        var index = Folders.FindIndex(listed => PinnedFolders.Same(listed, folder));
        if (index >= 0) MoveFolderTo(folder, index + offset);
    }

    /// <summary>Moves a folder to place <paramref name="index"/> in the list (clamped), its workspace with it: a drag's drop, and Move up and down.</summary>
    internal void MoveFolderTo(string folder, int index)
    {
        var folders = Folders.ToList();
        var from = folders.FindIndex(listed => PinnedFolders.Same(listed, folder));
        if (from < 0) return;
        var target = Math.Clamp(index, 0, folders.Count - 1);
        if (target == from) return;
        var moved = folders[from];
        folders.RemoveAt(from); folders.Insert(target, moved);
        SetFolderOrder(folders);
    }

    /// <summary>
    /// Puts the Folders list in <paramref name="ordered"/>'s order, which holds the same folders, and the workspaces with it, Scratch
    /// staying last: one order for both panels, the indicator's list and Ctrl+Alt+1 to 9. Saved; nothing happens for the order there is.
    /// </summary>
    private void SetFolderOrder(List<string> ordered)
    {
        if (ordered.SequenceEqual(Folders, StringComparer.OrdinalIgnoreCase)) return;
        _preferences.Folders = ordered;
        SavePreferences();
        var workspaces = ordered.Select(WorkspaceOfFolder).Where(workspace => workspace is not null).Cast<Workspace>().ToList();
        if (Scratch is { } scratch) workspaces.Add(scratch);
        _workspaces.Clear(); _workspaces.AddRange(workspaces);
        Diagnostics.Log("folders: reordered to " + string.Join(", ", ordered.Select(Paths.Leaf)));
        FoldersChanged();
    }

    /// <summary>
    /// The list changed, or a folder's emoji or color did: both panels follow, the indicator and the Sessions list's folder groups
    /// read the list again, and the layout is saved with the workspaces as they now stand.
    /// </summary>
    private void FoldersChanged()
    {
        RefreshFolderMarks();
        RefreshFoldersCard();
        RefreshFoldersPanel();
        RefreshWorkspacePanel();
        SaveLayout(); UpdateStatus();
        if (_history is not null && _root.IsLoaded) _ = SearchAsync();
    }

    /// <summary>
    /// Moves a tab, with its panes tiled as they are and their terminals running, to another workspace: at <paramref name="at"/>
    /// in that workspace's strip, or at the end. Into the current workspace it is selected; into a parked one it becomes the
    /// tab shown when that workspace is switched to, and the strip is switched there too with <paramref name="follow"/>. A tab
    /// moved to its own workspace is only shown. Refused for a closing tab and past <see cref="Workspaces.MaxTabs"/> tabs.
    /// The rule's own plumbing (<see cref="PlaceTab"/>, <see cref="AddFolder"/>, <see cref="RemoveFolder"/>): nothing offers it by hand.
    /// </summary>
    private void MoveTabToWorkspace(TabHost host, Workspace target, int? at = null, bool follow = false)
    {
        if (!_open.ContainsKey(host.Item)) throw new InvalidOperationException("That tab is no longer open.");
        if (host.Closing || _closingTabs.Contains(host.Item) || host.Panes.Any(pane => pane.Closing || _closingPanes.Contains(pane))) throw new InvalidOperationException("That tab is closing.");
        if (!_workspaces.Contains(target)) throw new InvalidOperationException("That workspace is gone.");
        var from = WorkspaceOf(host) ?? throw new InvalidOperationException("That tab is in no workspace.");
        if (ReferenceEquals(from, target)) { if (follow) ShowHost(host); return; }
        if (ItemsOf(target).Count() >= Workspaces.MaxTabs) throw new InvalidOperationException($"A workspace holds up to {Workspaces.MaxTabs} tabs.");
        if (host.Editor is not null) _ = EndRenameAsync(host, commit: false, refocus: false);
        var now = DateTimeOffset.UtcNow;
        _movingTab = true;
        try
        {
            // Taking the item out of the strip changes the selection, which saves and refreshes the panel on the way; the flag keeps a Scratch made for this tab until it lands.
            RemoveTabItem(host.Item);
            if (ReferenceEquals(target, _workspace))
            {
                _tabs.TabItems.Insert(at is { } place ? Math.Clamp(place, 0, _tabs.TabItems.Count) : _tabs.TabItems.Count, host.Item);
                _tabs.SelectedItem = host.Item;
            }
            else
            {
                target.Parked.Insert(at is { } place ? Math.Clamp(place, 0, target.Parked.Count) : target.Parked.Count, host.Item);
                target.SelectedTabId = host.Saved.Id;
                foreach (var pane in host.Panes) pane.Presence.Selected(false, now);
                RefreshPresence(host);
            }
        }
        finally { _movingTab = false; }
        if (follow && !ReferenceEquals(target, _workspace)) SwitchWorkspace(target);
        Diagnostics.Log("workspace: “" + DisplayTitle(host.Saved) + "” moved from “" + WorkspaceName(from) + "” to “" + WorkspaceName(target) + "”" + (follow ? ", followed" : ""));
        RefreshWorkspacePanel(); SaveLayout(); UpdateStatus(); RefreshConversationRows();
    }

    /// <summary>The workspace <paramref name="offset"/> places along from the current one, round the ends; the current one alone when there is one.</summary>
    private Workspace NeighbourWorkspace(int offset)
    {
        var count = _workspaces.Count;
        return _workspaces[((_workspaces.IndexOf(_workspace) + offset) % count + count) % count];
    }

    /// <summary>Ctrl+Alt+Left and Right: the previous or next workspace.</summary>
    private void StepWorkspace(int offset) { if (_workspaces.Count > 1) SwitchWorkspace(NeighbourWorkspace(offset)); }

    /// <summary>Ctrl+Alt+1 to 9: the workspace at that place in the list, 9 for the last.</summary>
    private void SelectWorkspaceByDigit(int digit)
    {
        var index = Workspaces.IndexForDigit(digit, _workspaces.Count);
        if (index >= 0) SwitchWorkspace(_workspaces[index]);
    }

    /// <summary>The most urgent state among a workspace's tabs (<see cref="PaneState"/> rises in urgency); idle for an empty one.</summary>
    private PaneState WorkspacePresence(Workspace workspace) => HostsOf(workspace).Select(host => host.Presence).DefaultIfEmpty(PaneState.Idle).Max();

    /// <summary>Whether a workspace out of view wants the user: an agent in it waits (Your turn, Needs you) or failed.</summary>
    private bool WorkspaceWantsUser(Workspace workspace) => !ReferenceEquals(workspace, _workspace)
        && HostsOf(workspace).Any(host => host.Presence is PaneState.Attention or PaneState.Blocked or PaneState.Error);

    /// <summary>The emoji of a workspace's folder, or null for Scratch and a folder without one.</summary>
    private string? WorkspaceMark(Workspace workspace) => workspace.Home is { } home ? _preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(home)) : null;

    /// <summary>
    /// A workspace's color: its home folder's own, as its emoji is, never a listed folder's above it (until November 11, 2026 a
    /// workspace without one took the nearest such folder's, so clearing a nested workspace's color showed its parent's instead).
    /// Null for none, and for Scratch.
    /// </summary>
    private string? WorkspaceColor(Workspace workspace) => workspace.Home is { } home ? FolderColors.Normalize(_preferences.FolderColor.GetValueOrDefault(PinnedFolders.Normalize(home))) : null;

    /// <summary>
    /// A switcher row tinted with its workspace's color, as the indicator is: a wash of it, stronger under the pointer, in the
    /// shade for the menu's surface (the theme's, unlike the strip's). The wash is translucent, so it lies over whatever the
    /// menu is drawn on. A workspace without a color keeps the menu's plain row.
    /// </summary>
    private void TintSwitcherRow(MenuFlyoutItem item, Workspace workspace)
    {
        if (ResolveColor(WorkspaceColor(workspace), !_theme.Light) is not { } hex) return;
        var color = Theme.Parse(hex);
        var (rest, hover, pressed) = _theme.Light ? (0.14, 0.22, 0.11) : (0.22, 0.32, 0.18);
        item.Background = new SolidColorBrush(Theme.WithAlpha(color, rest));
        Theme.Override(item.Resources, "MenuFlyoutItemBackgroundPointerOver", new SolidColorBrush(Theme.WithAlpha(color, hover)));
        Theme.Override(item.Resources, "MenuFlyoutItemBackgroundPressed", new SolidColorBrush(Theme.WithAlpha(color, pressed)));
    }

    /// <summary>The font a menu's icon draws an emoji in: an icon is drawn in the symbol font unless told otherwise, which has no emoji.</summary>
    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

    /// <summary>A menu item's icon for a workspace: its folder's emoji, else the folder glyph, or Scratch's note.</summary>
    private IconElement WorkspaceIcon(Workspace workspace) => WorkspaceMark(workspace) is { } mark ? new FontIcon { Glyph = mark, FontFamily = EmojiFont } : new FontIcon { Glyph = workspace.IsScratch ? ScratchGlyph : FolderGlyph, FontFamily = Theme.Icons };

    // ---- the rail's badge and the strip's indicator ----

    /// <summary>The dot on the rail's Workspaces button while a workspace out of view wants the user; refreshed with the tabs' dots.</summary>
    private void RefreshWorkspaceBadge()
    {
        if (_workspaceBadge is null) return;
        var wanted = _workspaces.Any(WorkspaceWantsUser);
        _workspaceBadge.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_railItems["workspaces"].Button, wanted ? "Agent Workspaces · an agent in another workspace needs you" : "Agent Workspaces");
    }

    /// <summary>
    /// The indicator at the head of the tab strip naming the current workspace: its folder's emoji, else the folder glyph (Scratch's
    /// note for Scratch), the name and a chevron: the name in bold when the workspace has a color, which the strip wears (the
    /// indicator alone did from November 10, 2026, a 2px line under the name before), a rounded hover inset from the strip's
    /// edges, and a divider before the tabs (docs/decisions/folder-colors.md). Shown while there is a listed
    /// folder or more than one workspace, and the strip has room for it (FitTabList). A click lists every workspace to switch to
    /// (<see cref="WorkspaceSwitcher"/>; a right-click opened it until November 9, 2026, when Chris had the click do it, since a
    /// switch is what the indicator is for), and so does Ctrl+Shift+A. The strip's head is a drag area, less the indicator (MainWindow.DragRegions.cs).
    /// </summary>
    private FrameworkElement BuildWorkspaceChip()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        _workspaceChipGlyph = Theme.Icon(FolderGlyph, 12, _chipInk);
        content.Children.Add(_workspaceChipGlyph);
        _workspaceChipMark = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        content.Children.Add(_workspaceChipMark);
        _workspaceChipText = new TextBlock { FontSize = 12, FontFamily = Theme.Body, Foreground = _chipInk, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis };
        content.Children.Add(_workspaceChipText);
        // The chevron says the indicator opens a list; it turns up while the list is open.
        _workspaceChipChevron = Theme.Icon(ChevronDownGlyph, 9, _chipInk);
        _workspaceChipChevron.Opacity = ChevronRest;
        _workspaceChipChevron.Margin = new Thickness(-1, 1, 0, 0);
        content.Children.Add(_workspaceChipChevron);
        // Option C of the Workspace selector polish mockup (November 11, 2026, Chris's pick): the hover is a 24 px rounded shape
        // set 4 px in from the strip's edges, so its edge lies on the strip and never against the first tab (the strip-high
        // square fill before met the darker inactive tab in a hard line), and a thin divider sets the workspace apart from the
        // tabs. The button itself still spans the strip's full height, so the whole head is a click target; only the pill paints.
        _workspaceChipPill = new Border
        {
            Child = content, Height = TabContentHeight, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 0, 10, 0),
            Background = _chipFill, VerticalAlignment = VerticalAlignment.Center
        };
        _workspaceChipDivider = new Border { Width = 1, Height = 16, Background = _chipDivider, Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var face = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(_workspaceChipPill);
        face.Children.Add(_workspaceChipDivider);
        _workspaceChip = new Button
        {
            Content = face, Padding = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(0), HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center, Background = _theme.Transparent, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch, Visibility = Visibility.Collapsed
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            Theme.Override(_workspaceChip.Resources, key, _theme.Transparent);
        // The pill follows the pointer and the list: brighter under the pointer and while the list is open, deeper pressed.
        _workspaceChip.PointerEntered += (_, _) => { _chipHovered = true; PaintChipPill(); };
        _workspaceChip.PointerExited += (_, _) => { _chipHovered = _chipHeld = false; PaintChipPill(); };
        _workspaceChip.PointerCaptureLost += (_, _) => { _chipHeld = false; PaintChipPill(); };
        _workspaceChip.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { _chipHeld = true; PaintChipPill(); }), handledEventsToo: true);
        _workspaceChip.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => { _chipHeld = false; PaintChipPill(); }), handledEventsToo: true);
        ToolTipService.SetToolTip(_workspaceChip, "The current agent workspace. Click to switch (Ctrl+Shift+A); Ctrl+Alt+1 to 9 and Ctrl+Alt+Left and Right switch too.");
        _workspaceChip.Flyout = WorkspaceSwitcher();
        _workspaceChip.Flyout.Opened += (_, _) => { _chipOpen = true; PaintChipPill(); };
        _workspaceChip.Flyout.Closed += (_, _) => { _chipOpen = false; PaintChipPill(); };
        return _workspaceChip;
    }

    private Border _workspaceChipPill = null!, _workspaceChipDivider = null!;
    private FontIcon _workspaceChipChevron = null!;
    private bool _chipHovered, _chipHeld, _chipOpen;
    private const string ChevronDownGlyph = "", ChevronUpGlyph = "";
    /// <summary>The chevron's opacity at rest; full under the pointer and while the list is open.</summary>
    private const double ChevronRest = 0.7;
    /// <summary>The divider between the indicator and the tabs: the strip's ink, faint (<see cref="PaintWorkspaceChip"/>).</summary>
    private readonly SolidColorBrush _chipDivider = new(Microsoft.UI.Colors.Transparent);

    /// <summary>The indicator's pill and chevron as the pointer and the list stand: pressed, lit (pointer or list open), or at rest.</summary>
    private void PaintChipPill()
    {
        if (_workspaceChipPill is null) return;
        var lit = _chipHovered || _chipOpen;
        var brush = _chipHeld ? _chipPressed : lit ? _chipHover : _chipFill;
        if (!ReferenceEquals(_workspaceChipPill.Background, brush)) _workspaceChipPill.Background = brush;
        _workspaceChipChevron.Glyph = _chipOpen ? ChevronUpGlyph : ChevronDownGlyph;
        _workspaceChipChevron.Opacity = lit ? 1 : ChevronRest;
    }

    /// <summary>Verification: the indicator's pill (its corner and height), whether the chevron shows and which way, and whether the divider shows.</summary>
    internal (CornerRadius Corner, double Height, bool ChevronUp, bool Divider, Brush Fill) WorkspaceChipFace =>
        (_workspaceChipPill.CornerRadius, _workspaceChipPill.Height, _workspaceChipChevron.Glyph == ChevronUpGlyph,
            _workspaceChipDivider.Visibility == Visibility.Visible, _workspaceChipPill.Background);

    /// <summary>
    /// Ctrl+Shift+A: the indicator's list, with the keyboard in it. With the indicator hidden (the strip too cramped for it) the
    /// Agent Workspaces panel opens instead; with one workspace and no folder there is nothing to switch to.
    /// </summary>
    private void ShowWorkspaceSwitcher()
    {
        if (_workspaceChip is null) return;
        if (_workspaceChip.Visibility == Visibility.Visible)
        {
            // The focus moves off a terminal (its WebView2) first, so the list takes the keys and hands them back to the indicator.
            _workspaceChip.Focus(FocusState.Keyboard);
            _workspaceChip.Flyout.ShowAt(_workspaceChip);
        }
        else if (_workspaces.Count > 1) OpenPanel("workspaces");
    }

    /// <summary>
    /// The indicator's list: every workspace by its mark and name with its tab count, tinted with its color as the indicator is
    /// (<see cref="TintSwitcherRow"/>, since November 11, 2026), the current one in bold, each switching the
    /// strip, and nothing else (Add folder… and Folders sat under them until November 9, 2026; the Folders panel has both). Built
    /// as it opens, so it reads the workspaces as they stand. Each row ends in its workspace's state as a tab shows its own
    /// (<see cref="AddSwitcherState"/>, since November 11, 2026), so where to go next shows with the sidebar closed.
    /// </summary>
    private MenuFlyout WorkspaceSwitcher()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            _switcherStates.Clear();
            foreach (var workspace in _workspaces)
            {
                var target = workspace;
                var described = Describe(workspace);
                var count = described.Tabs.Count;
                // A plain item, not a toggle: a toggle's check takes the column the icon shows in, which hid the emoji. The current one is in bold.
                var item = new MenuFlyoutItem
                {
                    Text = Workspaces.Name(described), Icon = WorkspaceIcon(workspace), KeyboardAcceleratorTextOverride = count == 0 ? "" : count == 1 ? "1 tab" : count + " tabs",
                    FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(ReferenceEquals(workspace, _workspace) ? 700 : 400) }
                };
                TintSwitcherRow(item, workspace);
                AddSwitcherState(item, workspace);
                item.Click += (_, _) => SwitchWorkspace(target);
                menu.Items.Add(item);
            }
            RefreshSwitcherStates();
        };
        // Its marks stop following the tabs once it closes; it is built afresh when it opens again.
        menu.Closed += (_, _) => _switcherStates.Clear();
        return menu;
    }

    /// <summary>The open switcher's rows and their state marks, kept current with the tabs (<see cref="RefreshSwitcherStates"/>); empty while it is closed.</summary>
    private readonly List<SwitcherState> _switcherStates = [];

    private sealed record SwitcherState(Workspace Workspace, MenuFlyoutItem Item, Grid Holder, Ellipse Dot, ProgressRing Ring);

    /// <summary>
    /// A switcher row's state mark, a dot or the working ring after the tab count: the menu item has no place for one, so a column
    /// is added to its template's grid once it is laid out. A template without that grid shows the row without the mark.
    /// </summary>
    private void AddSwitcherState(MenuFlyoutItem item, Workspace workspace)
    {
        var dot = Theme.Dot(_theme.SuccessDot, 8);
        dot.Visibility = Visibility.Collapsed;
        var ring = new ProgressRing
        {
            Width = 12, Height = 12, MinWidth = 0, MinHeight = 0, IsIndeterminate = true, IsActive = false, Foreground = _theme.Accent,
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed
        };
        var holder = new Grid { Width = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(dot);
        holder.Children.Add(ring);
        _switcherStates.Add(new SwitcherState(workspace, item, holder, dot, ring));
        item.Loaded += (_, _) =>
        {
            if (holder.Parent is not null || FindNamed<Grid>(item, "LayoutRoot") is not { } root) return;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(holder, root.ColumnDefinitions.Count - 1);
            root.Children.Add(holder);
        };
    }

    /// <summary>
    /// The open switcher's marks as the workspaces' tabs stand, as a tab paints its own (RefreshTabStatus): the ring while an agent
    /// works and nothing wants the user; else red for Needs you or Failed, yellow for Your turn or Done, green while anything runs,
    /// none otherwise; blinking on the tabs' beat while a tab in it blinks. The row's tooltip and name carry the state in words.
    /// </summary>
    private void RefreshSwitcherStates()
    {
        foreach (var state in _switcherStates)
        {
            var hosts = HostsOf(state.Workspace).ToList();
            var presence = WorkspacePresence(state.Workspace);
            var running = hosts.Any(host => host.Panes.Any(pane => pane.IsRunning));
            var wanted = presence is PaneState.Attention or PaneState.Blocked or PaneState.Done or PaneState.Error;
            var spinning = presence == PaneState.Working;
            state.Ring.IsActive = spinning;
            state.Ring.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;
            state.Dot.Visibility = !spinning && (running || wanted) ? Visibility.Visible : Visibility.Collapsed;
            state.Dot.Fill = presence switch
            {
                PaneState.Blocked or PaneState.Error => _theme.DangerDot,
                PaneState.Attention or PaneState.Done => _theme.WarningDot,
                _ => _theme.SuccessDot
            };
            state.Dot.Opacity = _blinkOffBeat && hosts.Any(Blinks) ? StateDots.BlinkDim : 1;
            var words = presence switch
            {
                PaneState.Blocked => "Needs you",
                PaneState.Attention => "Your turn",
                PaneState.Done => "Done",
                PaneState.Error => "Failed",
                PaneState.Working => "Working…",
                _ => running ? "Running" : null
            };
            var where = state.Workspace.Home ?? "Sessions outside every folder in Folders";
            var tip = words is null ? where : where + "\n" + words;
            // Only when it changed: the blink repaints these every beat, and a tooltip set again closes while it is read.
            if (ToolTipService.GetToolTip(state.Item) as string != tip) ToolTipService.SetToolTip(state.Item, tip);
            var spoken = words is null ? state.Item.Text : state.Item.Text + ", " + words;
            if (AutomationProperties.GetName(state.Item) != spoken) AutomationProperties.SetName(state.Item, spoken);
        }
    }

    /// <summary>Verification: the open switcher's rows, whether each one's mark is in its row, and what it shows (ring, dot or none, and the dot's brush).</summary>
    internal IReadOnlyList<(string Name, bool Placed, string Shown, Brush? Fill)> SwitcherStates => _switcherStates.Select(state =>
        (state.Item.Text, state.Holder.Parent is not null, state.Ring.Visibility == Visibility.Visible ? "ring" : state.Dot.Visibility == Visibility.Visible ? "dot" : "none",
            state.Dot.Visibility == Visibility.Visible ? state.Dot.Fill : null)).ToList();

    /// <summary>Verification: the indicator's list, opened and read.</summary>
    internal IReadOnlyList<MenuFlyoutItemBase> WorkspaceSwitcherItems()
    {
        var menu = (MenuFlyout)_workspaceChip.Flyout;
        menu.ShowAt(_workspaceChip);
        var items = menu.Items.ToList();
        menu.Hide();
        return items;
    }

    private void RefreshWorkspaceChip()
    {
        if (_workspaceChip is null || _workspace is null) return;
        var described = Describe(_workspace);
        _workspaceChipText.Text = Workspaces.Name(described);
        var mark = WorkspaceMark(_workspace);
        _workspaceChipMark.Text = mark ?? "";
        _workspaceChipMark.Visibility = mark is null ? Visibility.Collapsed : Visibility.Visible;
        _workspaceChipGlyph.Glyph = _workspace.IsScratch ? ScratchGlyph : FolderGlyph;
        _workspaceChipGlyph.Visibility = mark is null ? Visibility.Visible : Visibility.Collapsed;
        PaintWorkspaceChip(ResolveColor(WorkspaceColor(_workspace), _theme.StripDark));
        _workspaceChip.Visibility = !_chipCramped && (_workspaces.Count > 1 || Folders.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(_workspaceChip, "Workspace " + _workspaceChipText.Text);
    }

    /// <summary>
    /// Recolors the tab strip and the indicator for the workspace's folder color as RRGGBB, or null for none: with one, the whole
    /// strip behind and around the tabs takes a wash of it over the strip's surface (since November 11, 2026, at Chris's request;
    /// the indicator alone wore it before), the indicator stays clear on it, stronger under the pointer, its name in bold in the
    /// strip's ink, or dark ink should the wash be light, and the inactive tabs a darker shade of it (PaintTab); without, the
    /// strip's own surface and the plain button, its name in the
    /// strip's secondary ink.
    /// </summary>
    private void PaintWorkspaceChip(string? hex)
    {
        var strip = _theme.StripSurface;
        _chipDivider.Color = Theme.WithAlpha(_theme.StripText.Color, 0.28);
        _chipFill.Color = Microsoft.UI.Colors.Transparent;
        Windows.UI.Color? tint = null;
        if (hex is null)
        {
            if (!ReferenceEquals(_stripBackdrop.Background, _theme.Rail)) _stripBackdrop.Background = _theme.Rail;
            _chipHover.Color = _theme.StripHover.Color;
            _chipPressed.Color = _theme.StripPressed.Color;
            _chipInk.Color = _theme.StripTextSecondary.Color;
            _workspaceChipText.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }
        else
        {
            var color = Theme.Parse(hex);
            tint = Theme.Over(Theme.WithAlpha(color, StripWash), strip);
            // Frosted, the strip in the workspace's color is part-clear as the rail is; inks are still chosen against the solid tint.
            _stripTint.Color = _theme.Frosted ? Theme.WithAlpha(tint.Value, Theme.RailFrost) : tint.Value;
            if (!ReferenceEquals(_stripBackdrop.Background, _stripTint)) _stripBackdrop.Background = _stripTint;
            _chipHover.Color = Theme.Over(Theme.WithAlpha(color, ChipHoverWash), strip);
            _chipPressed.Color = Theme.Over(Theme.WithAlpha(color, ChipPressedWash), strip);
            _chipInk.Color = Theme.Readable(_chipHover.Color, _theme.StripText.Color, Theme.Ink(_chipHover.Color));
            _workspaceChipText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
        // The inactive tabs take a darker shade of the strip's color (PaintTab), so they are repainted when it changes.
        if (tint == StripTint) return;
        StripTint = tint;
        PaintTabs();
    }

    /// <summary>The tab strip's surface while the current workspace has a color, which the inactive tabs darken (PaintTab); null for the theme's own strip.</summary>
    private Windows.UI.Color? StripTint { get; set; }

    /// <summary>Verification: the strip's surface, and the indicator's fill and ink, as they are painted.</summary>
    internal (Windows.UI.Color Strip, Windows.UI.Color Fill, Windows.UI.Color Ink) WorkspaceChipPaint =>
        ((_stripBackdrop.Background as SolidColorBrush)?.Color ?? default, _chipFill.Color, _chipInk.Color);

    // ---- the panel ----

    /// <summary>
    /// The Workspaces panel: a row per workspace, in the Folders list's order with Scratch last. Each row is open to its sessions,
    /// a row apiece, until its chevron folds it to a second line (the tab count, then the folder), the agents' tiles, and a dot in
    /// the most urgent state among its tabs; only the chevron folds or opens a row, and the fold is saved. One with nothing open
    /// is dimmed. A click switches; the row's menu has New session here, the folder's emoji, color and sound, Consolidate tabs, Close all
    /// tabs and Remove from Folders. The panel's own title names it, so the list carries no header of its own, and no words under it: a description sat there until Chris had it out on November 8, 2026.
    /// </summary>
    private UIElement BuildWorkspacesPanel()
    {
        var grid = new Grid { Padding = new Thickness(12, 6, 12, 12), RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _workspaceList = new StackPanel { Spacing = 2, Padding = new Thickness(0, 0, 0, PanelFootBuffer) };
        _workspaceScroll = new ScrollViewer { Content = WithDropLine(_workspaceList, _workspaceDropLine), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(-6, 0, -6, 0) };
        grid.Children.Add(_workspaceScroll);
        return grid;
    }

    private ScrollViewer _workspaceScroll = null!;
    private readonly Border _workspaceDropLine = new() { Height = 2, CornerRadius = new CornerRadius(1), Visibility = Visibility.Collapsed, IsHitTestVisible = false };

    /// <summary>The listed folders' rows in the Agent Workspaces panel, in the list's order, Scratch's left out: where a dragged row may land.</summary>
    private IReadOnlyList<(string Folder, FrameworkElement Container, FrameworkElement Row)> WorkspaceDragRows() => _workspaces
        .Where(workspace => workspace.Home is not null && _workspaceRows.ContainsKey(workspace.Id))
        .Select(workspace => (workspace.Home!, (FrameworkElement)_workspaceRows[workspace.Id].Container, (FrameworkElement)_workspaceRows[workspace.Id].Row)).ToList();

    /// <summary>A workspace's row and the rows of its tabs under it, with the parts that change as the tabs do.</summary>
    private sealed class WorkspaceRowView
    {
        internal required Workspace Workspace { get; init; }
        internal required StackPanel Container { get; init; }
        internal required Grid Row { get; init; }
        internal required Border Bar { get; init; }
        internal required FontIcon Chevron { get; init; }
        internal required TextBlock Mark { get; init; }
        internal required FontIcon Glyph { get; init; }
        internal required TextBlock Name { get; init; }
        internal required TextBlock Summary { get; init; }
        internal required StackPanel Tiles { get; init; }
        internal required TextBlock Count { get; init; }
        internal required Ellipse Dot { get; init; }
        internal required StackPanel Tabs { get; init; }
        internal required Button Fold { get; init; }
        /// <summary>The line the session rows hang from, under the workspace's row; shown with <see cref="Tabs"/>.</summary>
        internal required Border Guide { get; init; }
        internal List<(TabHost Host, Grid Row, Ellipse Dot, TextBlock Name)> TabRows { get; } = [];
        /// <summary>The rows of the panes after a tab's first, each under its tab's row.</summary>
        internal List<(TabHost Host, SessionPane Pane, Grid Row, Ellipse Dot, TextBlock Name)> PaneRows { get; } = [];
        internal bool Hovered { get; set; }
    }

    /// <summary>Everything the rows are built from, the tabs and their panes: a change rebuilds them (<see cref="RefreshWorkspacePanel"/>); otherwise only their states are brought up to date.</summary>
    private string WorkspacePanelSignature() => string.Join('\u001e', _workspaces.Select(workspace =>
        workspace.Id + '\u001f' + (ReferenceEquals(workspace, _workspace) ? "*" : "") + (WorkspaceExpanded(workspace) ? "+" : "")
        + '\u001f' + string.Join('\u001d', HostsOf(workspace).Where(host => !host.Closing).Select(host => host.Saved.Id + ":" + string.Join(',', host.Panes.Where(pane => !pane.Closing).Select(pane => pane.Descriptor.Id))))));

    /// <summary>Brings the panel in line with the workspaces: Scratch trimmed when it is empty and out of view, rows rebuilt when a workspace or a tab came, went or moved, and their states refreshed either way.</summary>
    internal void RefreshWorkspacePanel()
    {
        if (_workspaceList is null || _workspace is null) return;
        if (!_restoring && !_switchingWorkspace) TrimScratch();
        var signature = WorkspacePanelSignature();
        if (signature != _workspacePanelSignature)
        {
            _workspacePanelSignature = signature;
            _workspaceList.Children.Clear();
            _workspaceRows.Clear();
            foreach (var workspace in _workspaces)
            {
                var view = BuildWorkspaceRow(workspace);
                _workspaceRows[workspace.Id] = view;
                _workspaceList.Children.Add(view.Container);
            }
        }
        RefreshWorkspaceStates();
    }

    /// <summary>The rows' names, second lines, tiles, counts and dots, the tab rows' names and dots and which is selected, the indicator, the badge, and the Folders panel's badges.</summary>
    private void RefreshWorkspaceStates()
    {
        if (_workspaceList is null || _workspace is null) return;
        foreach (var view in _workspaceRows.Values)
        {
            var workspace = view.Workspace;
            var described = Describe(workspace);
            var current = ReferenceEquals(workspace, _workspace);
            var expanded = WorkspaceExpanded(workspace);
            var empty = described.Tabs.Count == 0;
            var name = Workspaces.Name(described);
            if (view.Name.Text != name) view.Name.Text = name;
            // A workspace with nothing open is dimmed: a place to go rather than a thing that is happening.
            view.Name.Foreground = empty && !current ? _theme.TextSecondary : _theme.Text;
            // A workspace with nothing open is its name alone: the dimming says it is empty and the tooltip names its folder.
            var summary = empty ? "" : Workspaces.Summary(described);
            if (view.Summary.Text != summary) view.Summary.Text = summary;
            view.Summary.Visibility = expanded || empty ? Visibility.Collapsed : Visibility.Visible;
            view.Count.Text = described.Tabs.Count > 0 ? described.Tabs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            view.Count.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            var mark = WorkspaceMark(workspace);
            view.Mark.Text = mark ?? "";
            view.Mark.Visibility = mark is null ? Visibility.Collapsed : Visibility.Visible;
            view.Glyph.Visibility = mark is null ? Visibility.Visible : Visibility.Collapsed;
            view.Glyph.Opacity = view.Mark.Opacity = empty && !current ? 0.6 : 1;
            view.Chevron.Glyph = expanded ? "" : "";
            // Nothing to fold, so no chevron; its place is kept so the workspaces' marks stay in a column.
            view.Fold.Opacity = empty ? 0 : 1;
            view.Fold.IsHitTestVisible = view.Fold.IsTabStop = !empty;
            // The agents' tiles say what is inside while the tabs are not listed, each agent once.
            view.Tiles.Children.Clear();
            if (!expanded) foreach (var agent in described.Tabs.Select(tab => tab.Panes[0].AgentId).Distinct(StringComparer.Ordinal).Take(6)) view.Tiles.Children.Add(_theme.AgentTile(agent, 12));
            var presence = WorkspacePresence(workspace);
            var wanted = presence is PaneState.Attention or PaneState.Blocked or PaneState.Done or PaneState.Error;
            var running = HostsOf(workspace).Any(host => host.Panes.Any(pane => pane.IsRunning));
            view.Dot.Visibility = running || wanted ? Visibility.Visible : Visibility.Collapsed;
            view.Dot.Fill = presence switch
            {
                PaneState.Blocked or PaneState.Error => _theme.DangerDot,
                PaneState.Attention or PaneState.Done => _theme.WarningDot,
                PaneState.Working => _theme.Accent,
                _ => _theme.SuccessDot
            };
            view.Bar.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
            // A workspace with a color has its name and its folder glyph in it, and its bar while current; nothing is filled.
            var rowColor = RowColor(workspace.Home);
            view.Bar.Background = rowColor is { } barColor ? RowBar(barColor) : _theme.Accent;
            if (rowColor is { } rowInk) view.Name.Foreground = RowBar(rowInk);
            view.Glyph.Foreground = rowColor is { } glyphInk ? RowBar(glyphInk) : _theme.TextSecondary;
            view.Row.Background = current ? _theme.Layer : view.Hovered ? _theme.Hover : _theme.Transparent;
            // Put away during a row drag, the fold unchanged (BeginFolderDrag).
            view.Tabs.Visibility = view.Guide.Visibility = expanded && !empty && !FolderDragging ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(view.Row, name + "\n" + (workspace.Home ?? "Sessions outside every folder in Folders") + (empty ? "" : "\n" + Workspaces.Summary(described)));
            AutomationProperties.SetName(view.Row, "Workspace " + name + (current ? ", current" : ""));
            foreach (var (host, row, dot, title) in view.TabRows)
            {
                var label = TabLabel(host.Saved);
                if (title.Text != label) title.Text = label;
                dot.Fill = host.Ring.IsActive ? _theme.Accent : host.Dot.Fill;
                dot.Visibility = host.Ring.IsActive || host.Dot.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
                // The tab's own row is lit while the tab shows, unless the keyboard is in one of its later panes, whose row is lit instead.
                var selected = current && ReferenceEquals(_tabs.SelectedItem, host.Item) && !(_activePane is { } active && host.Panes.IndexOf(active) > 0);
                row.Background = selected ? _theme.Layer : _theme.Transparent;
            }
            foreach (var (host, pane, row, dot, title) in view.PaneRows)
            {
                if (title.Text != pane.Label) title.Text = pane.Label;
                var state = pane.PresenceState;
                dot.Visibility = pane.IsRunning || state is PaneState.Attention or PaneState.Blocked or PaneState.Done or PaneState.Error ? Visibility.Visible : Visibility.Collapsed;
                dot.Fill = state switch
                {
                    PaneState.Blocked or PaneState.Error => _theme.DangerDot,
                    PaneState.Attention or PaneState.Done => _theme.WarningDot,
                    PaneState.Working => _theme.Accent,
                    _ => _theme.SuccessDot
                };
                row.Background = current && ReferenceEquals(_tabs.SelectedItem, host.Item) && ReferenceEquals(_activePane, pane) ? _theme.Layer : _theme.Transparent;
            }
        }
        RefreshWorkspaceChip();
        RefreshWorkspaceBadge();
        RefreshSwitcherStates();
        RefreshFolderStates();
    }

    private WorkspaceRowView BuildWorkspaceRow(Workspace workspace)
    {
        var row = new Grid { ColumnSpacing = 6, MinHeight = 30, Padding = new Thickness(4, 3, 8, 3), CornerRadius = new CornerRadius(4), Background = _theme.Transparent };
        for (var column = 0; column < 6; column++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = column == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = _theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(-4, 4, 0, 4), Visibility = Visibility.Collapsed };
        Grid.SetColumnSpan(bar, 6); row.Children.Add(bar);
        var chevron = Theme.Icon("", 9, _theme.TextSecondary);
        var fold = new Button { Content = chevron, Width = 20, Height = 20, Padding = new Thickness(0), Background = _theme.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(fold, "Show or hide this workspace's tabs");
        AutomationProperties.SetName(fold, "Show or hide the tabs");
        fold.Click += (_, _) => ToggleWorkspaceFold(workspace);
        row.Children.Add(fold);
        var markHost = new Grid { Width = 18, VerticalAlignment = VerticalAlignment.Center };
        var mark = new TextBlock { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var glyph = Theme.Icon(workspace.IsScratch ? ScratchGlyph : FolderGlyph, 14, _theme.TextSecondary);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        markHost.Children.Add(mark); markHost.Children.Add(glyph);
        Grid.SetColumn(markHost, 1); row.Children.Add(markHost);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        var name = _theme.BodyText("", 14, 600); name.TextTrimming = TextTrimming.CharacterEllipsis;
        var summary = _theme.Tertiary(""); summary.FontSize = 11; summary.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name); text.Children.Add(summary);
        Grid.SetColumn(text, 2); row.Children.Add(text);
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tiles, 3); row.Children.Add(tiles);
        var count = _theme.Tertiary("");
        Grid.SetColumn(count, 4); row.Children.Add(count);
        var dot = Theme.Dot(_theme.Neutral, 7); dot.Visibility = Visibility.Collapsed;
        Grid.SetColumn(dot, 5); row.Children.Add(dot);
        // The sessions hang from a line under the workspace's chevron and start past the edge of its name, so the folders read as
        // the list's headings and their sessions as what is under them; a little air after the last sets the next workspace apart.
        var tabs = new StackPanel { Visibility = Visibility.Collapsed };
        var guide = new Border { Child = tabs, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(14, 0, 0, 6), Visibility = Visibility.Collapsed };
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        container.Children.Add(row); container.Children.Add(guide);
        var view = new WorkspaceRowView { Workspace = workspace, Container = container, Row = row, Bar = bar, Chevron = chevron, Mark = mark, Glyph = glyph, Name = name, Summary = summary, Tiles = tiles, Count = count, Dot = dot, Tabs = tabs, Guide = guide, Fold = fold };
        row.Tapped += (_, args) =>
        {
            if (FolderDragJustEnded || args.OriginalSource is DependencyObject source && Ancestor<Button>(source) is not null) return;
            ForgetSelectedFolder();
            SwitchWorkspace(workspace);
            // The row clicked is the folder Ctrl+Shift+E opens, until a pane is clicked.
            _explorerFolder = workspace.Home;
        };
        // A folder's row drags to another place in the list, which both panels share; Scratch stays last, so its row does not.
        if (workspace.Home is { } home) MakeFolderRowDraggable(row, home, _workspaceList, _workspaceScroll, _workspaceDropLine, WorkspaceDragRows);
        row.ContextFlyout = WorkspaceMenu(view);
        row.PointerEntered += (_, _) => { view.Hovered = true; RefreshWorkspaceStates(); };
        row.PointerExited += (_, _) => { view.Hovered = false; RefreshWorkspaceStates(); };
        foreach (var host in HostsOf(workspace).Where(host => !host.Closing)) AddWorkspaceTabRows(view, host);
        return view;
    }

    /// <summary>
    /// A session's row under a workspace: an agent's tile, a name, a detail at the right (the folder) and a dot, indented by
    /// <paramref name="indent"/> from the guide line.
    /// </summary>
    private (Grid Row, TextBlock Name, Ellipse Dot) SessionRow(double indent, string agentId, string title, string detail, double tileSize, double textSize)
    {
        var row = new Grid { ColumnSpacing = 6, MinHeight = 24, Padding = new Thickness(indent, 2, 10, 2), CornerRadius = new CornerRadius(4), Background = _theme.Transparent };
        for (var column = 0; column < 4; column++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = column == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var tile = _theme.AgentTile(agentId, tileSize); tile.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(tile);
        var name = _theme.BodyText(title, textSize); name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1); row.Children.Add(name);
        // The folder takes the width it needs, up to half the row, so the session's name always keeps the other half.
        var folder = _theme.Tertiary(detail); folder.FontSize = 11;
        Grid.SetColumn(folder, 2); row.Children.Add(folder);
        row.SizeChanged += (_, args) => folder.MaxWidth = Math.Max(0, args.NewSize.Width / 2);
        var dot = Theme.Dot(_theme.Neutral, 6); dot.Visibility = Visibility.Collapsed;
        Grid.SetColumn(dot, 3); row.Children.Add(dot);
        row.PointerEntered += (_, _) => { if (row.Background == _theme.Transparent) row.Background = _theme.Hover; };
        row.PointerExited += (_, _) => RefreshWorkspaceStates();
        return (row, name, dot);
    }

    /// <summary>
    /// A tab's rows under its workspace: the tab's own, with its first pane's tile, its name, its folder and the tab's dot, and
    /// under it, indented once more, a row for each pane after the first (a split tab is several sessions, and each shows), with
    /// the pane's tile, name, its folder where that differs from the tab's, and its own dot. A click shows the tab, and gives the
    /// keyboard to that row's pane; the tab's own menu opens on a right-click of any of them.
    /// </summary>
    private void AddWorkspaceTabRows(WorkspaceRowView view, TabHost host)
    {
        var first = host.Saved.Panes[0];
        var tabFolder = first.WorkingDirectory is { Length: > 0 } directory ? Paths.Leaf(directory) : "";
        var (row, name, dot) = SessionRow(24, first.AgentId, TabLabel(host.Saved), tabFolder, 14, 13);
        ToolTipService.SetToolTip(row, DisplayTitle(host.Saved) + "\n" + first.WorkingDirectory);
        AutomationProperties.SetName(row, "Tab " + DisplayTitle(host.Saved));
        row.Tapped += (_, _) => { ShowHost(host); if (host.Panes.Count > 1) SetActivePane(host.Panes[0]); };
        row.RightTapped += (_, args) => { args.Handled = true; if (host.Item.ContextFlyout is MenuFlyout menu) menu.ShowAt(row, new FlyoutShowOptions { Position = args.GetPosition(row) }); };
        view.TabRows.Add((host, row, dot, name));
        view.Tabs.Children.Add(row);
        foreach (var pane in host.Panes.Skip(1).Where(pane => !pane.Closing))
        {
            var shown = pane;
            var folder = pane.Descriptor.WorkingDirectory is { Length: > 0 } own && !PinnedFolders.Same(own, first.WorkingDirectory) ? Paths.Leaf(own) : "";
            var (paneRow, paneName, paneDot) = SessionRow(44, pane.Descriptor.AgentId, pane.Label, folder, 12, 12);
            ToolTipService.SetToolTip(paneRow, pane.Label + "\n" + pane.Descriptor.WorkingDirectory + "\nA pane of “" + DisplayTitle(host.Saved) + "”");
            AutomationProperties.SetName(paneRow, "Pane " + pane.Label);
            paneRow.Tapped += (_, _) => { ShowHost(host); if (host.Panes.Contains(shown)) { SetActivePane(shown); shown.FocusInput(); } };
            paneRow.RightTapped += (_, args) => { args.Handled = true; if (host.Item.ContextFlyout is MenuFlyout menu) menu.ShowAt(paneRow, new FlyoutShowOptions { Position = args.GetPosition(paneRow) }); };
            view.PaneRows.Add((host, pane, paneRow, paneDot, paneName));
            view.Tabs.Children.Add(paneRow);
        }
    }

    /// <summary>
    /// The items a listed folder's menus share, in the Workspaces panel and the Folders panel: Emoji… (Windows' emoji panel over
    /// the unseen box; docs/decisions/folder-emoji.md), Remove emoji, Color… (the swatch flyout at <paramref name="anchor"/>;
    /// folder-colors.md), Sound… (the picker flyout; completion-sounds.md) and Open in File Explorer.
    /// </summary>
    private void AddFolderDressing(MenuFlyout menu, string folder, FrameworkElement anchor)
    {
        var mark = _preferences.FolderEmoji.GetValueOrDefault(PinnedFolders.Normalize(folder));
        var emoji = new MenuFlyoutItem { Text = mark is null ? "Emoji…" : "Change emoji…", Icon = mark is null ? new FontIcon { Glyph = "", FontFamily = Theme.Icons } : new FontIcon { Glyph = mark, FontFamily = EmojiFont } };
        ToolTipService.SetToolTip(emoji, "Opens Windows' emoji panel; the emoji chosen stands for this folder on its rows and at the head of the tab strip.");
        emoji.Click += async (_, _) => await OpenEmojiPanelAsync(folder);
        menu.Items.Add(emoji);
        if (mark is not null)
        {
            var remove = new MenuFlyoutItem { Text = "Remove emoji", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
            remove.Click += (_, _) => SetFolderEmoji(folder, null);
            menu.Items.Add(remove);
        }
        var color = new MenuFlyoutItem { Text = "Color…", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
        ToolTipService.SetToolTip(color, "Tints the workspace's indicator at the head of the tab strip, and the swatch on its Sessions group.");
        color.Click += (_, _) => ShowFolderColorFlyout(anchor, folder);
        menu.Items.Add(color);
        var sound = new MenuFlyoutItem { Text = "Sound…", Icon = new FontIcon { Glyph = SpeakerGlyph, FontFamily = Theme.Icons } };
        ToolTipService.SetToolTip(sound, "Plays when an agent in this folder, or under it, is ready, instead of the sound in Settings.");
        sound.Click += (_, _) => ShowFolderSoundFlyout(anchor, folder);
        menu.Items.Add(sound);
        var explorer = new MenuFlyoutItem { Text = "Open in File Explorer", Icon = new FontIcon { Glyph = ExplorerGlyph, FontFamily = Theme.Icons } };
        explorer.Click += (_, _) => OpenInExplorer(folder);
        menu.Items.Add(explorer);
    }

    /// <summary>The Move up and Move down items for a listed folder, in either panel; a drag of the row does the same.</summary>
    private void AddFolderOrder(MenuFlyout menu, string folder)
    {
        var index = Folders.FindIndex(listed => PinnedFolders.Same(listed, folder));
        var up = new MenuFlyoutItem { Text = "Move up", IsEnabled = index > 0, Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
        up.Click += (_, _) => MoveFolder(folder, -1);
        menu.Items.Add(up);
        var down = new MenuFlyoutItem { Text = "Move down", IsEnabled = index >= 0 && index < Folders.Count - 1, Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
        down.Click += (_, _) => MoveFolder(folder, 1);
        menu.Items.Add(down);
    }

    /// <summary>Remove from Folders, in either panel: the folder leaves the list, its tabs staying open where the rule now puts them.</summary>
    private MenuFlyoutItem RemoveFolderItem(string folder)
    {
        var remove = new MenuFlyoutItem { Text = "Remove from Folders", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
        ToolTipService.SetToolTip(remove, "Its tabs stay open and move to the folder above it in the list, or to Scratch. Its emoji, color and sound are kept for the day it is listed again.");
        remove.Click += (_, _) => { try { RemoveFolder(folder); } catch (Exception exception) { ShowError(exception.Message); } };
        return remove;
    }

    /// <summary>
    /// The workspace row's menu: New session here, Conversations in this workspace; its folder's emoji, color, sound and Explorer,
    /// and its place in the list; Resume workspace tabs (Ctrl+F5), Consolidate tabs (F6, the tabs of this workspace that
    /// share a folder) and Close all tabs; and
    /// Remove from Folders. Scratch has the session items alone. Built as it opens so the items read the state then.
    /// </summary>
    private MenuFlyout WorkspaceMenu(WorkspaceRowView view)
    {
        var workspace = view.Workspace;
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var start = new MenuFlyoutItem { Text = "New session here", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
            start.Click += async (_, _) => { SwitchWorkspace(workspace); await ShowNewSessionAsync(workspace.Home ?? NewSessionFolder); };
            menu.Items.Add(start);
            if (workspace.Home is { } home)
            {
                var conversations = new MenuFlyoutItem { Text = "Conversations in this workspace", Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
                ToolTipService.SetToolTip(conversations, "Show only conversations from the folder and its subfolders");
                conversations.Click += async (_, _) => { await SetScopeAsync(home); OpenPanel("sessions"); };
                menu.Items.Add(conversations);
                menu.Items.Add(new MenuFlyoutSeparator());
                AddFolderDressing(menu, home, view.Row);
                menu.Items.Add(new MenuFlyoutSeparator());
                AddFolderOrder(menu, home);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var hosts = HostsOf(workspace).Where(host => !host.Closing).ToList();
            var resume = new MenuFlyoutItem { Text = "Resume workspace tabs", KeyboardAcceleratorTextOverride = "Ctrl+F5", Icon = ResumeAllMark.Icon() };
            ToolTipService.SetToolTip(resume, "Starts the sessions waiting in this workspace's tabs and leaves other workspaces' waiting.");
            resume.IsEnabled = !ResumingAll && HasResumable(workspace);
            resume.Click += async (_, _) => await ResumeWorkspaceFromUserAsync(workspace, new("Resume workspace tabs on the workspace's right-click menu", DateTimeOffset.UtcNow));
            menu.Items.Add(resume);
            var consolidate = new MenuFlyoutItem { Text = "Consolidate tabs", KeyboardAcceleratorTextOverride = "F6", Icon = new FontIcon { Glyph = ConsolidateGlyph, FontFamily = Theme.Icons } };
            ToolTipService.SetToolTip(consolidate, "Every tab of this workspace that shares a folder with another joins the first of them, as panes.");
            consolidate.IsEnabled = TabConsolidation.Groups(hosts, host => host.Panes.Count > 0 ? host.Panes[0].Descriptor.WorkingDirectory : null).Count > 0;
            consolidate.Click += (_, _) => { SwitchWorkspace(workspace); ConsolidateTabsFromUser(); };
            menu.Items.Add(consolidate);
            var closeAll = new MenuFlyoutItem { Text = "Close all tabs", IsEnabled = hosts.Count > 0, Icon = new FontIcon { Glyph = "", FontFamily = Theme.Icons } };
            ToolTipService.SetToolTip(closeAll, "Closes this workspace's tabs, asking about live agents as a tab close does.");
            closeAll.Click += async (_, _) => { try { await CloseTabsAsync(ItemsOf(workspace).ToList()); } catch (Exception exception) { ShowError(exception.Message); } };
            menu.Items.Add(closeAll);
            if (workspace.Home is { } listed)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(RemoveFolderItem(listed));
            }
        };
        return menu;
    }

    /// <summary>The folder a new session starts in by default: the focused pane's, else the current workspace's folder, else the first listed folder, else the user's profile.</summary>
    private string NewSessionFolder => _activePane?.Descriptor.WorkingDirectory is { Length: > 0 } active ? active
        : _workspace?.Home ?? Folders.FirstOrDefault() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// A new session asked for in a folder outside every listed one, by the New session dialog: the folder may join Folders, so
    /// this session and later ones there have a workspace of their own, or the session opens in Scratch. Cancel starts nothing.
    /// Under the smoke, which cannot answer a dialog, the session goes to Scratch. Returns whether to go on.
    /// </summary>
    private async Task<bool> OfferFolderAsync(string folder)
    {
        if (RootFor(folder) is not null || _options.SmokeTest) return true;
        var leaf = Paths.Leaf(folder);
        var text = _theme.BodyText(folder + " is not in Folders. Adding it creates a workspace for this session and later ones in that folder. Open in Scratch keeps the session with others from unlisted folders.", 14);
        text.TextWrapping = TextWrapping.Wrap; text.MaxWidth = 460;
        var dialog = Dialog("Add this folder to Folders?", text, "Add " + leaf);
        dialog.SecondaryButtonText = "Open in Scratch";
        dialog.CloseButtonText = "Cancel";
        var answer = await dialog.ShowAsync();
        if (answer == ContentDialogResult.Primary) { AddFolder(folder); return true; }
        return answer == ContentDialogResult.Secondary;
    }
}
