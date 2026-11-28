using System.Text.Json.Serialization;
using Sic.Core.Agents;
using Sic.Core.Appearance;
using Sic.Core.Terminals;

namespace Sic.Core.State;

/// <summary>
/// One pane of a tab. <see cref="Title"/> is the conversation's own title as it was when the pane opened; <see cref="CustomTitle"/>
/// is a name given to the pane while it has no saved conversation to keep the name against (a shell, or an agent pane not yet
/// linked), and is carried over to <see cref="AppPreferences.ConversationNames"/> once it is linked. What the pane shows is
/// <see cref="Appearance.TabLabels.PaneTitle"/>. <see cref="Unsaved"/> marks a session SIC started whose conversation the agent
/// has not yet saved: nothing was sent in it as far as SIC knows, so it starts again in its folder when there is no history
/// to resume (<see cref="Sessions.FreshStart"/>).
/// </summary>
public sealed record SavedPane(string Id, string AgentId, string Title, string WorkingDirectory,
    string HistoryDirectory, string? SessionId = null, string? CreationName = null, string? CustomTitle = null, bool Unsaved = false);
/// <summary>
/// A tab holds one to <see cref="PaneLayout.MaxPanes"/> panes, listed in the order of <see cref="Layout"/>, the split tree that
/// tiles them (<see cref="PaneLayout"/>). <see cref="SplitRatio"/> and <see cref="RowRatio"/> are how a tab saved before there
/// were trees recorded its one column and one row divider; they are read when <see cref="Layout"/> is missing and no longer
/// written. <see cref="CustomTitle"/> is the tab's own name; without one the tab shows its first pane's name
/// (<see cref="Appearance.TabLabels.TabTitle"/>). <see cref="Color"/> is a <see cref="Appearance.TabColors"/> preset id or
/// custom RRGGBB, or null for none. <see cref="KeepEven"/> is whether the tab keeps every pane the same share of its room
/// (<see cref="PaneLayout.Even"/>) through splits and closes: on for a new tab, off once a divider is dragged, and either way
/// from the tab's menu. A tab saved before the choice existed (null) keeps even when none of its dividers has been dragged
/// (<see cref="PaneLayout.Untouched"/>), and keeps its own ratios otherwise; the answer is written from then on.
/// <see cref="Zoomed"/> is the <see cref="SavedPane.Id"/> of the pane shown as a card over the others (<see cref="Appearance.PaneZoom"/>),
/// or null, not written, while none is; an id the tab no longer holds reads as none. The zoom is the window's: while any tab has
/// a zoomed pane, every tab shown zooms its focused pane, so a layout with one comes back zoomed.
/// </summary>
public sealed record SavedTab(string Id, string Title, List<SavedPane> Panes, string? CustomTitle = null, double? SplitRatio = null, string? Color = null,
    double? RowRatio = null, PaneNode? Layout = null, bool? KeepEven = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Zoomed = null);

/// <summary>
/// A workspace: a group of tabs with a tab strip of its own, one per folder in the Folders
/// list (docs/decisions/workspaces.md). <see cref="Home"/> is that folder, which names it (<see cref="Workspaces.Name"/>) and
/// decides which tabs are its (<see cref="Workspaces.RootFor"/>); null for Scratch, the workspace of sessions outside every
/// listed folder. <see cref="SelectedTabId"/> is the tab showing in it. <see cref="Tabs"/> is in strip order and may be empty.
/// <see cref="Name"/> was a name given by hand, until November 8, 2026; it is read and no longer written or shown.
/// </summary>
public sealed record SavedWorkspace(string Id, List<SavedTab> Tabs, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null, string? Home = null, string? SelectedTabId = null);

/// <summary>
/// layout.json: what is open. Since November 7, 2026 the tabs are grouped in <see cref="Workspaces"/>, and <see cref="Tabs"/> and
/// <see cref="SelectedTabId"/> are the current workspace's, still written so a build from before restores that workspace at
/// least; a file without <see cref="Workspaces"/> is read as one workspace holding <see cref="Tabs"/> (<see cref="State.Workspaces.Read"/>).
/// </summary>
public sealed class WorkspaceLayout
{
    public int Version { get; set; } = 1;
    public string? SelectedTabId { get; set; }
    public List<SavedTab> Tabs { get; set; } = [];
    /// <summary>Every workspace with its tabs, in the Workspaces panel's order; null in a file from before there were workspaces.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SavedWorkspace>? Workspaces { get; set; }
    /// <summary>The workspace whose tabs the strip shows.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedWorkspaceId { get; set; }
}

public sealed class AppPreferences
{
    public int Version { get; set; } = 1;
    /// <summary>A <see cref="Appearance.ThemeCatalog"/> identifier. The original "Dark"/"Light" values are still accepted.</summary>
    public string Theme { get; set; } = "sic-dark";
    /// <summary>Whether the rail's panel is open, and which panel it shows: sessions, workspaces, or settings.</summary>
    public bool PanelOpen { get; set; } = true;
    public string ActivePanel { get; set; } = "sessions";
    /// <summary>The panel's width in pixels when open, as last dragged. Clamped by <see cref="PanelLayout.ClampWidth"/> on load.</summary>
    public int PanelWidth { get; set; } = PanelLayout.DefaultWidth;
    /// <summary>
    /// A <see cref="Shell.CloseAndMinimize"/> word: whether closing and minimizing the window hide it to the tray, or close quits.
    /// Normalized on load. It replaces <c>MinimizeToTray</c>, which is no longer read, so every copy starts with both going to the tray.
    /// </summary>
    public string CloseAndMinimize { get; set; } = Shell.CloseAndMinimize.Tray;
    /// <summary>
    /// When true, closing a tab, a pane or the app sends each live agent its exit command without asking first. On by default
    /// since October 16, 2026, at Chris's request, so a new install never asks the Exit cleanly / Force / Cancel question; a
    /// settings file that records the value keeps it, off unless it was turned on, and one from before the switch existed reads as on.
    /// </summary>
    public bool AlwaysExitCleanly { get; set; } = true;
    /// <summary>
    /// When true, the placeholders a launch restores are resumed at once, tab by tab (Sessions.ResumeAll), instead of a notice
    /// offering Resume all. Off by default: restoring never started an agent, and a launch that starts thirty-two is a choice.
    /// </summary>
    public bool ResumeAllOnLaunch { get; set; }
    /// <summary>
    /// A <see cref="MotionSetting"/> word, "on" or "off": whether the panel slides open and shut (Settings → Menu Animation),
    /// whatever Windows says. Normalized on load.
    /// </summary>
    public string Motion { get; set; } = MotionSetting.On;
    /// <summary>A <see cref="SidebarSide"/> word: which side of the window the rail and its panel sit on (Settings → Side Panel). Normalized on load.</summary>
    public string Sidebar { get; set; } = SidebarSide.Left;
    /// <summary>
    /// When true, the rail is put away while the panel is shut and comes out over the terminals while the pointer is at its edge,
    /// as in full screen (Settings → Autohide sidebar). Off by default (November 11, 2026, at Chris's request).
    /// </summary>
    public bool SidebarAutoHide { get; set; }
    /// <summary>
    /// The frosted chrome as an early build (0.9.52-trial.1) saved it, a switch. Read once into <see cref="Chrome"/>,
    /// then cleared, and a null is not written.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FrostedChrome { get; set; }
    /// <summary>
    /// A <see cref="Appearance.ChromeStyle"/> word: whether the rail, the panel, the tab strip and the status bar are solid,
    /// frosted or clear (Settings → Chrome). Normalized on load. Solid by default (November 12, 2026).
    /// </summary>
    public string Chrome { get; set; } = Appearance.ChromeStyle.Solid;
    /// <summary>
    /// How solid the whole window is, in percent. 100 is solid; less lets what is behind SicTerm show through all of it, the terminals
    /// and their text included (Settings → Window opacity). Clamped on load. Since November 12, 2026.
    /// </summary>
    public int WindowOpacity { get; set; } = 100;
    /// <summary>A <see cref="TabBarSide"/> word: which edge of the terminal area the tab bar runs along. Normalized on load.</summary>
    public string TabBar { get; set; } = TabBarSide.Top;
    /// <summary>
    /// How wide the accent border is round the pane with the keyboard, in DIPs (Settings → Session Indicator Width). Clamped on
    /// load by <see cref="Appearance.PaneBorder.Clamp"/>.
    /// </summary>
    public int PaneBorderWidth { get; set; } = Appearance.PaneBorder.Default;
    /// <summary>
    /// The border's color (Settings → Session Indicator Color): null, not written, for the theme's accent; else a tab color
    /// preset id or RRGGBB. Normalized on load by <see cref="Appearance.PaneBorder.NormalizeColor"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaneBorderColor { get; set; }
    /// <summary>
    /// The width as 0.9.26 and 0.9.27 saved it, a word: "thin", "medium" or "thick". Read once into <see cref="PaneBorderWidth"/>
    /// (<see cref="Appearance.PaneBorder.FromWord"/>), then cleared, and a null is not written.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaneBorder { get; set; }
    /// <summary>The zoomed pane's card, in percent of the terminal area's width and height (Settings → Zoomed pane size). Clamped on load by <see cref="Appearance.PaneZoom.Clamp"/>.</summary>
    public int PaneZoomPercent { get; set; } = Appearance.PaneZoom.DefaultPercent;
    /// <summary>A <see cref="SettingsTabs"/> word: the Settings tab last shown. Normalized on load.</summary>
    public string SettingsTab { get; set; } = SettingsTabs.General;
    /// <summary>An <see cref="Appearance.AppIcon"/> option: the icon on the taskbar, the title bar and the tray. Normalized on load.</summary>
    public string AppIcon { get; set; } = Appearance.AppIcon.Default;
    /// <summary>
    /// When true, a soft chime sounds when an agent comes to rest after a turn the user started (<see cref="PresenceTracker.TurnsEnded"/>).
    /// Off by default: a sound nobody asked for is worse than none.
    /// </summary>
    public bool ReadyChime { get; set; }
    /// <summary>The window's last restored bounds and maximized state; null until the first launch has placed it. Fitted to the display on load.</summary>
    public WindowPlacement? Window { get; set; }
    public string FontFamily { get; set; } = "Cascadia Mono, Consolas, monospace";
    public int FontSize { get; set; } = 14;
    /// <summary>A <see cref="TerminalOptions.CursorStyles"/> value; normalized on load.</summary>
    public string CursorStyle { get; set; } = TerminalOptions.DefaultCursorStyle;
    public bool CursorBlink { get; set; } = true;
    /// <summary>Lines of scrollback per pane; clamped by <see cref="TerminalOptions.ClampScrollback"/> on load.</summary>
    public int Scrollback { get; set; } = TerminalOptions.DefaultScrollback;
    /// <summary>
    /// When true, releasing the mouse after selecting terminal text copies it. Off by default: with it on, every selection
    /// replaces the clipboard, which surprises anyone who selects only to read.
    /// </summary>
    public bool CopyOnSelect { get; set; }
    /// <summary>
    /// When true, text of several lines pasted into a PowerShell pane, which runs each line as it arrives, is shown first and sent
    /// only on Paste (Settings → Terminal; docs/decisions/clipboard.md). On by default, as Windows Terminal's warning is; agent
    /// panes take a paste into their prompt and are never asked. A file without it, older than the switch, reads as on.
    /// </summary>
    public bool ConfirmMultiLinePaste { get; set; } = true;
    /// <summary>
    /// When true, an image a program shows through kitty's Unicode placeholders (Hermes's pet, in its kitty render mode) is drawn
    /// over the placeholder cells (docs/decisions/pet-images.md). On by default: without such a program it costs next to nothing. A file
    /// without it, older than the switch, reads as on; an older build reading it ignores it.
    /// </summary>
    public bool PetImages { get; set; } = true;
    public string PreferredAgent { get; set; } = "hermes";
    /// <summary>Agent filter for the conversation list; null shows every agent.</summary>
    public string? AgentFilter { get; set; }
    /// <summary>Folder scope for the conversation list; null shows every folder.</summary>
    public string? ScopeFolder { get; set; }
    /// <summary>
    /// A <see cref="Sessions.ConversationListing"/> grouping word: how the conversation list is sectioned, by folder unless
    /// chosen otherwise (docs/decisions/sessions-groups.md). Normalized on load; a file from before the choice reads as by folder.
    /// </summary>
    public string GroupBy { get; set; } = Sessions.ConversationListing.ByFolder;
    /// <summary>A <see cref="Sessions.ConversationListing"/> sort word: newest first, or by name. Normalized on load.</summary>
    public string SortBy { get; set; } = Sessions.ConversationListing.Newest;
    /// <summary>The sections folded down to their headers, by key (<see cref="State.SectionFolds"/>); cleaned on load.</summary>
    public HashSet<string> FoldedSections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AgentDefinition> Agents { get; set; } = [];
    /// <summary>
    /// Whether an agent that is off was switched off by the user. False in settings saved before October 19, 2026, whose offs
    /// may be detection's; those are made undecided once at launch (<see cref="BuiltInAgents.ForgetDetectedOffs"/>).
    /// </summary>
    public bool AgentOffsAreChoices { get; set; }
    /// <summary>
    /// The Folders panel's list: the folders you work in, first added first, each the root of a workspace of the same name whose
    /// tabs are the sessions in it or under it (docs/decisions/workspaces.md; <see cref="Workspaces.RootFor"/>). Cleaned as
    /// <see cref="State.PinnedFolders"/> are on load. Null in a file from before November 8, 2026, which <see cref="Workspaces.AdoptFolders"/>
    /// fills once from the saved workspaces' homes and the older lists below.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Folders { get; set; }
    /// <summary>
    /// The Workplaces list a file from before November 8, 2026 kept: the folders the user added to the panel of that name. Read
    /// once into <see cref="Folders"/> by <see cref="Workspaces.AdoptFolders"/> and then cleared, so it is never written again.
    /// </summary>
    [JsonConverter(typeof(WorkplaceFolders.ListConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Workplaces { get; set; }
    /// <summary>
    /// The same list as a file from before November 7, 2026 kept it, under the key <c>Workspaces</c>: folder paths, or the named
    /// groups of folders an older file still kept. Read by <see cref="Workspaces.AdoptFolders"/> after <see cref="Workplaces"/> and then cleared.
    /// </summary>
    [JsonPropertyName("Workspaces")]
    [JsonConverter(typeof(WorkplaceFolders.ListConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyWorkspaces { get; set; }
    /// <summary>
    /// Conversations pinned to the top of the list, keyed like <see cref="ConversationNames"/>. Replaces the unused
    /// <c>Favorites</c> set, which nothing could fill; an old file's empty <c>Favorites</c> is ignored.
    /// </summary>
    public HashSet<string> PinnedConversations { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Tabs of two or more panes pinned whole (docs/decisions/tab-groups.md): each as layout.json would save it, under the tab's
    /// own id, listed under Pinned in Sessions and brought back as a tab from there; cleaned by <see cref="State.TabGroups.Clean"/> on load.
    /// </summary>
    public List<SavedTab> TabGroups { get; set; } = [];
    /// <summary>
    /// The folders a file from before November 8, 2026 kept pinned at the top of the Workplaces panel, first pin first. Read once
    /// into <see cref="Folders"/> (<see cref="Workspaces.AdoptFolders"/>), ahead of the Workplaces list, and the list is cleared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? PinnedFolders { get; set; }
    /// <summary>
    /// Every folder <see cref="AutoPins"/> has added to <see cref="Folders"/> (a pin, before November 8, 2026), kept after the folder is
    /// removed so a later run from the history does not add it again; cleaned as the folders are on load. The key keeps its old name.
    /// </summary>
    public List<string> AutoPinnedFolders { get; set; } = [];
    /// <summary>Whether the first run of <see cref="AutoPins"/>, after the first history refresh, has happened; it runs once.</summary>
    public bool AutoPinsDone { get; set; }
    /// <summary>
    /// The version of the welcome (the first-run film and card, docs/decisions/first-run.md) last shown on this PC; 0 before any.
    /// A launch whose number is below the app's shows it once more, so a release with a new chapter can show it again.
    /// </summary>
    public int WelcomeSeen { get; set; }
    /// <summary>
    /// The setup notice's gaps (<see cref="Shell.SetupAdvisor"/>) closed at launch: a launch shows only the others, so a closed
    /// notice never comes back at launch, and one left open shows again at the next.
    /// </summary>
    public Shell.SetupGap SetupClosedAtLaunch { get; set; }
    /// <summary>The gaps the Get set up panel showed on the home screen when its X closed it there: it comes back only for a gap that was not among them.</summary>
    public Shell.SetupGap SetupClosedOnHome { get; set; }
    /// <summary>
    /// Settings → Agents → PowerShell's extra arguments, for PowerShell tabs only (<see cref="Shell.ShellStatus.Arguments"/>):
    /// agents start through a script of SicTerm's own, and an argument such as -NoProfile there would hide an agent kept in a
    /// profile function from detection.
    /// </summary>
    public string[] ShellArguments { get; set; } = [];
    /// <summary>The home screen's Folders card was closed with its X: it shows no more, and Add folders stays on the Folders panel.</summary>
    public bool FoldersCardClosed { get; set; }
    /// <summary>
    /// Whether the status bar shows the plan limits of Claude and Codex while one of their panes runs (docs/decisions/usage-limits.md),
    /// and Claude Code is started with SIC as its status line command to report them. On by default.
    /// </summary>
    public bool ShowUsageLimits { get; set; } = true;
    /// <summary>
    /// An emoji per folder, keyed as <see cref="State.PinnedFolders"/> keys a folder and shown, for a folder in <see cref="Folders"/>,
    /// on its rows and on its workspace's indicator at the head of the tab strip; cleaned by <see cref="FolderMarks.Clean"/> on load,
    /// which also restores the case-insensitive keys. Given to pinned folders until November 8, 2026; the key stays with the folder.
    /// </summary>
    public Dictionary<string, string> FolderEmoji { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// The completion sound (Settings → Alerts): a <see cref="Alerts.SoundCatalog"/> id, a built-in recipe's word or a file name from
    /// the sounds folder; the chime by default, normalized on load. A listed folder's own sound (<see cref="FolderSound"/>) wins for panes in it or under it.
    /// </summary>
    public string Sound { get; set; } = "chime";
    /// <summary>A sound per folder, keyed as <see cref="FolderEmoji"/> is and read for the listed folders; cleaned by <see cref="FolderSounds.Clean"/> on load.</summary>
    public Dictionary<string, string> FolderSound { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// A color per folder, keyed as <see cref="FolderEmoji"/> is and read for the listed folders: the line under its workspace's indicator
    /// and the swatch on its Sessions group (on every tab in it until November 8, 2026); cleaned by <see cref="FolderColors.Clean"/> on load.
    /// </summary>
    public Dictionary<string, string> FolderColor { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Local display names keyed by exact agent and session ID; agent histories stay authoritative.
    public Dictionary<string, string> ConversationNames { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Tab colors keyed the same way as <see cref="ConversationNames"/>, so reopening a conversation brings its color back.</summary>
    public Dictionary<string, string> ConversationColors { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// The folder SIC started or linked each conversation in, keyed the same way as <see cref="ConversationNames"/>. The agent's
    /// own history comes first; this answers when it records none, as with a Hermes session bound before Hermes wrote its folder.
    /// </summary>
    public Dictionary<string, string> ConversationFolders { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Recently chosen custom tab colors as RRGGBB, newest first, offered as extra swatches.</summary>
    public List<string> RecentTabColors { get; set; } = [];
}
