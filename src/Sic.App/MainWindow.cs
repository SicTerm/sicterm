using Sic.App.Controls;
using Sic.Core.Alerts;
using Sic.Core.Appearance;
using Sic.Core.Agents;
using Sic.Core.Sessions;
using Sic.Core.Shell;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.System;
using Path = System.IO.Path;

namespace Sic.App;

/// <summary>
/// The shell: a 48 px rail that opens one panel at a time, on the window's left or right (ApplySidebarSide), the tab strip
/// living in the title-bar row (the way Windows Terminal does it) or above the status bar (ApplyTabBarSide), the terminal area,
/// and a slim status bar. The window drags from every part of the chrome with nothing to click (see MainWindow.DragRegions.cs).
/// F11 fills the display, with the rail and the panel away at their edge (MainWindow.FullScreen.cs).
/// </summary>
internal sealed partial class MainWindow : Window
{
    private const double RailWidth = 48;
    // The panel slides: opening decelerates and closing accelerates, the Fluent enter and exit pairing, both fast enough to read as one motion.
    private static readonly TimeSpan PanelOpenDuration = TimeSpan.FromMilliseconds(160), PanelCloseDuration = TimeSpan.FromMilliseconds(120);
    // A notice slides up from under the status bar with the same pairing; a brief one holds this long and slides back down.
    private static readonly TimeSpan NoticeInDuration = TimeSpan.FromMilliseconds(200), NoticeOutDuration = TimeSpan.FromMilliseconds(160);
    internal static TimeSpan BriefNoticeHold { get; set; } = TimeSpan.FromSeconds(4);
    // A tab hugs what it holds: its 24 px row of agent tile, title and close button is laid into the 20 px between the template's
    // 3 px padding and 1 px border, 4 px over the bottom edge (CreateTab, FitCloseButton), so a tab is 28 px, none of it goes
    // to the flared feet WinUI gives a selected tab, which are cut off (TrimTabFeet), and the tile and the title's letters sit
    // 4 px clear of the color stripe along the tab's top edge rather than against it. The strip adds 4 px above the tabs
    // (StripPadding, in place of the template's 8), the title bar's room to drag the window by, and so is exactly as tall as
    // Windows' caption buttons, 32 px; along the bottom (Settings → Tabs) it adds nothing, and is only as tall as its tabs.
    // From October 15 to 16, 2026 a tab was 24 px round a 20 px row, under the template's 8 px; before that it was 28 px,
    // the row inside the padding, with the feet in its last 4 px, and the strip 36.
    private const double TabContentHeight = 24;
    private const double TabHeight = 28;
    private const double TabContentOverhang = TabContentHeight - (TabHeight - 8);
    private const double StripPadding = 4;
    private const double StripHeight = StripPadding + TabHeight;
    private const double BottomStripHeight = TabHeight;
    private readonly StartupOptions _options;
    private readonly JsonStateStore<AppPreferences> _preferencesStore;
    private readonly JsonStateStore<WorkspaceLayout> _layoutStore;
    private readonly AppPreferences _preferences;
    private readonly ConversationCatalog _catalog;
    private readonly HistoryIndexer _indexer;
    private readonly Theme _theme;
    private readonly Grid _root = new();
    private readonly Grid _titleBar = new();
    // The strip's end after the tabs: the drag area and the caption-button spacer, or SIC's window buttons with the bars at the bottom.
    private readonly Grid _stripFooter = new() { Height = StripHeight, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Border _captionSpacer = new() { Width = 0 };
    private readonly Border _stripBackdrop = new() { Height = StripHeight, VerticalAlignment = VerticalAlignment.Top };
    /// <summary>The window's base under the terminal area while the chrome is frosted or clear (Settings → Chrome); nothing otherwise.</summary>
    private readonly Border _workBacking = new();
    private WindowBackdrop _backdrop = null!;
    private readonly ColumnDefinition _panelColumn = new() { Width = GridLength.Auto };
    // The body: the rail, the panel and the terminal area in three columns, above the status bar.
    private readonly Grid _body = new();
    // The body's outer columns, the rail's and the terminal area's, which trade places when the rail moves to the other side.
    private readonly ColumnDefinition _leadColumn = new(), _trailColumn = new();
    private readonly Grid _rail = new() { Width = RailWidth };
    private Grid _work = null!;
    // The mark at the top of the rail, and its place at the head of the tab strip, the window's top-left corner, while the
    // rail is on the right, where the window's close button covers the rail's top.
    private Border _railMark = null!, _stripMark = null!;
    private readonly Dictionary<string, (Button Button, Border Pill)> _railItems = [];
    private readonly Dictionary<string, Border> _railFrames = [];
    // The slot the panel slides in: its width is what animates, and it clips the full-width body pinned to its right edge.
    private readonly Grid _panel = new();
    private readonly Grid _panelBody = new();
    // The open panel's width: the body's width at all times, and the slot's while the panel is open and at rest.
    private double _panelWidth;
    private PaneSplitter _panelEdge = null!;
    /// <summary>Whether the panel slides open and shut: Settings → Menu Animation. The notices, the working ring and the Your turn blink always move.</summary>
    private bool PanelSlides => MotionSetting.Resolve(_preferences.Motion);
    /// <summary>
    /// Whether closing the window hides it to the tray rather than quitting: unless Settings → Close and minimize says close quits,
    /// or there is no tray icon to bring it back from.
    /// </summary>
    private bool CloseGoesToTray => _tray is not null && CloseAndMinimize.ClosesToTray(_preferences.CloseAndMinimize);
    /// <summary>Whether the rail and its panel sit on the window's right (Settings → Side Panel).</summary>
    private bool SidebarRight => SidebarSide.IsRight(_preferences.Sidebar);
    /// <summary>
    /// Whether the bars run along the bottom (Settings → Tabs): the tab bar above the status bar rather than in the title-bar row,
    /// each pane's header under its terminal, and the window's own buttons at the strip's end, with no title bar.
    /// </summary>
    private bool TabsBottom => TabBarSide.IsBottom(_preferences.TabBar);
    private bool _panelOpen = true;
    private Storyboard? _panelMotion;
    private readonly ContentControl _panelContent = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Dictionary<string, (string Title, UIElement Content)> _panels = [];
    private readonly InfoBar _notice = new() { IsClosable = true, Margin = new Thickness(8, 0, 8, 6) };
    // The notice slides within this host, which clips it, so a brief notice comes up from under the status bar and goes back down.
    private readonly Grid _noticeHost = new();
    private readonly TranslateTransform _noticeSlide = new();
    private Storyboard? _noticeMotion;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _noticeTimer;
    private int _noticeGeneration;
    // What the last notice said and whether it was brief, for the smoke test, which cannot count on catching a brief one open.
    internal (InfoBarSeverity Severity, string Title, string Message, bool Brief)? LastNotice { get; private set; }
    // No + button since October 9, 2026: New session is on the rail (Ctrl+Shift+T), and the strip keeps its width for tabs.
    private readonly TabView _tabs = new() { TabWidthMode = TabViewWidthMode.SizeToContent, VerticalAlignment = VerticalAlignment.Stretch, CanReorderTabs = true, IsAddTabButtonVisible = true };
    private readonly Dictionary<TabViewItem, TabHost> _open = [];
    private readonly List<TextBlock> _displayText = [];
    private readonly Dictionary<string, bool> _available = [];
    private readonly CancellationTokenSource _lifetime = new();
    // The strip's head (the inset before the first tab, or the mark with the sidebar on the right) and the panel header with its
    // Hide button: drag areas, less the button.
    private StackPanel _stripHeader = null!;
    // TabView's strip grid, tab list and its items, found on the first layout after the template is applied (FitTabList).
    private (Grid Strip, ListView List, ItemsPresenter Items)? _tabListParts;
    private Grid _panelHeader = null!;
    private Button _panelHide = null!;
    private TextBlock _panelTitle = null!;
    /// <summary>The rail's Resume all glyph, inked in the rail accent while there is something to resume (see UpdateStatus).</summary>
    private Shape _resumeGlyph = null!;
    private Ellipse _statusDot = null!;
    private TextBlock _statusText = null!;
    private TextBlock _statusDetail = null!;
    private Grid _welcome = null!;
    private SessionPane? _activePane;
    private TrayIcon? _tray;
    /// <summary>Where the tray icon's entry says it sits once SIC has looked (1 on the taskbar, 0 hidden), or null when Windows made none.</summary>
    private Task<int?> _trayPlacement = Task.FromResult<int?>(null);
    private nint _taskbarIcon, _titleBarIcon;
    private int _taskbarIconSize, _titleBarIconSize;
    private CancellationTokenSource? _searchCancellation;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _presenceTimer;
    private bool _started, _allowClose, _closing;

    internal MainWindow(StartupOptions options)
    {
        _options = options;
        Title = options.SmokeTest ? "SicTerm — verification fixtures" : "SicTerm";
        _preferencesStore = new(Path.Combine(options.DataDirectory, "settings.json"));
        _layoutStore = new(Path.Combine(options.DataDirectory, "layout.json"));
        StartDeferredSaves();
        _preferences = _preferencesStore.Load(CreateDefaults);
        if (_preferences.Version != 1 || _preferences.Agents is null || _preferences.ConversationNames is null
            || _preferences.ConversationColors is null || _preferences.ConversationFolders is null || _preferences.RecentTabColors is null)
            throw new InvalidDataException("Unsupported settings file. The existing file has not been changed.");
        // A settings file saved before an agent was built in lists only the agents of its day; the new ones are offered too.
        _preferences.Agents = BuiltInAgents.Merge(_preferences.Agents, CreateDefaults().Agents);
        // Until October 19, 2026 detection switched off an agent it did not find, which kept it off once installed; such an
        // off is undecided again, once, and is on as soon as detection finds the agent (docs/decisions/agent-switches.md).
        if (!_preferences.AgentOffsAreChoices)
        {
            _preferences.AgentOffsAreChoices = true;
            if (BuiltInAgents.ForgetDetectedOffs(_preferences.Agents) is { } forgotten) { _preferences.Agents = forgotten; SavePreferences(); }
        }
        _preferences.Theme = ThemeCatalog.Normalize(_preferences.Theme);
        _preferences.PanelWidth = PanelLayout.ClampWidth(_preferences.PanelWidth);
        _preferences.CursorStyle = TerminalOptions.NormalizeCursorStyle(_preferences.CursorStyle);
        _preferences.Scrollback = TerminalOptions.ClampScrollback(_preferences.Scrollback);
        _preferences.Motion = MotionSetting.Normalize(_preferences.Motion);
        _preferences.GroupBy = ConversationListing.NormalizeGrouping(_preferences.GroupBy);
        _preferences.SortBy = ConversationListing.NormalizeSort(_preferences.SortBy);
        _preferences.FoldedSections = SectionFolds.Clean(_preferences.FoldedSections);
        if (_preferences.Folders is not null) _preferences.Folders = PinnedFolders.Clean(_preferences.Folders);
        _preferences.AutoPinnedFolders = PinnedFolders.Clean(_preferences.AutoPinnedFolders);
        _preferences.TabGroups = TabGroups.Clean(_preferences.TabGroups);
        _preferences.FolderEmoji = FolderMarks.Clean(_preferences.FolderEmoji);
        _preferences.FolderSound = FolderSounds.Clean(_preferences.FolderSound);
        _preferences.FolderColor = FolderColors.Clean(_preferences.FolderColor);
        _preferences.Sound = SoundCatalog.Normalize(_preferences.Sound);
        _preferences.Sidebar = SidebarSide.Normalize(_preferences.Sidebar);
        _preferences.TabBar = TabBarSide.Normalize(_preferences.TabBar);
        // A width saved by 0.9.26 or 0.9.27 as a word is read once into pixels, and not written again.
        if (_preferences.PaneBorder is { } borderWord) { _preferences.PaneBorderWidth = PaneBorder.FromWord(borderWord); _preferences.PaneBorder = null; }
        _preferences.PaneBorderWidth = PaneBorder.Clamp(_preferences.PaneBorderWidth);
        _preferences.PaneZoomPercent = PaneZoom.Clamp(_preferences.PaneZoomPercent);
        _preferences.WindowOpacity = ClampWindowOpacity(_preferences.WindowOpacity);
        // The Frosted chrome switch of an early build (0.9.52-trial.1) is read once into the Chrome choice, and not written again.
        if (_preferences.FrostedChrome is { } frosted) { if (frosted) _preferences.Chrome = ChromeStyle.Frosted; _preferences.FrostedChrome = null; }
        _preferences.Chrome = ChromeStyle.Normalize(_preferences.Chrome);
        _preferences.PaneBorderColor = PaneBorder.NormalizeColor(_preferences.PaneBorderColor);
        _preferences.CloseAndMinimize = CloseAndMinimize.Normalize(_preferences.CloseAndMinimize);
        _preferences.SettingsTab = SettingsTabs.Normalize(_preferences.SettingsTab);
        // The shipped icon is the only one since October 8, 2026 (MainWindow.AppIcon.cs); a trial choice saved before is dropped here.
        _preferences.AppIcon = AppIcon.Default;
        _panelWidth = _preferences.PanelWidth;
        PlaceWindow();
        if (!options.SmokeTest) TrackWindowPlacement();
        // The user's theme files, read before the saved theme is looked up, so a theme of the user's own comes back at launch
        // (docs/decisions/theme-files.md); a file that cannot be used is logged here and shown under Settings → Theme.
        foreach (var problem in ThemeCatalog.Reload(Path.Combine(options.DataDirectory, "themes"))) Diagnostics.Log("theme file " + problem.File + ": " + problem.Error);
        foreach (var problem in SoundCatalog.Reload(Path.Combine(options.DataDirectory, "sounds"))) Diagnostics.Log("sound file " + problem.File + ": " + problem.Error);
        _theme = new Theme(ThemeCatalog.Find(_preferences.Theme)) { AgentIcons = new AgentIconCache(Path.Combine(options.DataDirectory, "agent-icons")), Frosted = ChromeStyle.SeeThrough(_preferences.Chrome) };
        if (_theme.Frosted) _theme.Apply(_theme.Definition);
        SystemBackdrop = _backdrop = new WindowBackdrop(WinRT.Interop.WindowNative.GetWindowHandle(this), _preferences.Chrome, _theme.Base.Color);
        _catalog = new ConversationCatalog(Path.Combine(options.DataDirectory, "catalog.db"));
        _indexer = new HistoryIndexer(_catalog);
        CreateUpdates();
        BuildShell();
        Content = _root;
        ExtendsContentIntoTitleBar = true;
        ApplyWindowOpacity();
        ApplyCaptionButtons();
        TrackDragRegions();
        TrackWindowButtons();
        ApplyThemeChrome();
        // The old folder lists (pinned, Workplaces) and the saved workspaces' homes fill the Folders list here too (Workspaces.AdoptFolders), on the first launch that finds them.
        RestoreLayout(_layoutStore.Load(() => new WorkspaceLayout()));
        UpdateStatus();

        _appIconPath = AppIconPath(ShellIcons.LightTaskbar());
        if (_preferences.AppIcon != AppIcon.Default) SetWindowIdentity();
        if (File.Exists(_appIconPath)) { AppWindow.SetIcon(_appIconPath); SetShellIcons(_appIconPath); }
        try
        {
            _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this),
                () => DispatcherQueue.TryEnqueue(ShowFromTray),
                () => DispatcherQueue.TryEnqueue(async () =>
                {
                    // An exception here would end the app through the unhandled-exception handler, taking every agent down unasked.
                    try { ShowFromTray(); await CloseAsync(); }
                    catch (Exception exception) { ShowError(exception.Message); }
                }),
                () => CloseAndMinimize.MinimizesToTray(_preferences.CloseAndMinimize) && !_closing && !_options.RestoreOnly,
                () => { SaveLayout(); AppWindow.Hide(); },
                _appIconPath,
                () => { SaveLayout(); foreach (var pane in AllPanes.Where(item => item.IsRunning)) _ = pane.TryExitAsync(TimeSpan.FromSeconds(3)); },
                ApplyAppIcon,
                text => DispatcherQueue.TryEnqueue(async () => await OpenRequestedAsync(OpenRequest.Decode(text))),
                EnvironmentChanged);
            SingleInstance.Tag(WinRT.Interop.WindowNative.GetWindowHandle(this), options.DataDirectory);
            if (!options.RestoreOnly) _trayPlacement = TrayIcon.PlaceOnTaskbarAsync();
        }
        catch (System.ComponentModel.Win32Exception exception) { ShowError(exception.Message + " Closing the window quits SicTerm."); }
        _root.Loaded += (_, _) => UpdateCaptionInset();
        _root.SizeChanged += (_, _) => UpdateCaptionInset();
        _root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            try
            {
                if (_options.RestoreOnly) { await RunRestoreCheckAsync(); return; }
                if (_options.Measure is { } measure) { await MeasureHiddenOutputAsync(measure); return; }
                if (_options.SmokeTest) CreateSmokeHistory();
                // The browser that every terminal runs in is brought up now, so the first session does not wait for it.
                TerminalView.WarmUp(Path.Combine(_options.DataDirectory, "WebView2"));
                // The list from the catalog as it stands, at once; the refresh below brings it up to date and lists again
                // (which leaves the rows alone when nothing changed). It used to wait for the refresh, which can take seconds
                // when the day's transcripts have grown.
                _ = SearchAsync();
                // The restored placeholders, resumed at once when Settings says so. A resume that needs the history refreshed
                // waits for the one under way below, so this runs alongside it rather than after it.
                ResumeRestoredSessions();
                // A first run: the film plays over the tab area while detection and the refresh run; its card waits for their facts.
                if (WelcomeDue) _ = ShowWelcomeAsync();
                await Task.WhenAll(DetectToolsAsync(), RefreshHistoryAsync());
                SendWelcomeContext();
                StartUpdateChecks();
                FollowShellTone();
                // Started by Open in SIC: the dialog, or the session, once detection has said which agents are here and the layout is back.
                if (_options.Open is { } open) await OpenRequestedAsync(open);
                if (_options.SmokeTest) await RunSmokeTestAsync();
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
                if (_options.SmokeTest)
                {
                    Environment.ExitCode = 1;
                    _options.WriteReport(new { success = false, error = exception.ToString() });
                    await CloseAsync(force: true);
                }
            }
        };
        // Settings → Close and minimize (docs/decisions/close-and-minimize.md). Quitting asks about live agents as Quit does, with
        // the window shown for the question, as a close from a minimized window's taskbar thumbnail would leave it out of sight.
        AppWindow.Closing += (sender, args) =>
        {
            if (_allowClose) return;
            args.Cancel = true;
            Diagnostics.Log("window close requested: " + (CloseGoesToTray ? "hiding to the tray" : "quitting"));
            if (CloseGoesToTray) { SaveLayout(); AppWindow.Hide(); return; }
            if (AllPanes.Any(pane => pane.IsRunning)) ShowFromTray();
            _ = CloseAsync();
        };
    }

    private AppPreferences CreateDefaults()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var hermesHome = Environment.GetEnvironmentVariable("HERMES_HOME");
        if (string.IsNullOrWhiteSpace(hermesHome)) hermesHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes");
        var preferences = new AppPreferences { Agents = BuiltInAgents.Create(home, hermesHome).ToList() };
        // No folders on a first run: a workspace's home is only what the user sets, since no folder exists on every machine. The
        // smoke seeds the folder list a settings file kept before November 8, 2026, which the first launch adopts as the first
        // workspace's home (Workspaces.Adopt), so the merge's one-time read is exercised.
        if (_options.SmokeTest) preferences.Workplaces = [_options.DataDirectory];
        if (_options.SmokeTest)
            preferences.Agents = preferences.Agents.Select(agent => agent with { HistoryDirectory = Path.Combine(_options.DataDirectory, "fixtures", agent.Id), Enabled = true }).ToList();
        return preferences;
    }

    private void BuildShell()
    {
        _root.RequestedTheme = _theme.Light ? ElementTheme.Light : ElementTheme.Dark;
        _theme.Accentuate(_root.Resources);
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = _body;
        body.ColumnDefinitions.Add(_leadColumn);
        body.ColumnDefinitions.Add(_panelColumn);
        body.ColumnDefinitions.Add(_trailColumn);
        body.Children.Add(BuildRail());
        var panel = BuildPanel(); Grid.SetColumn(panel, 1); body.Children.Add(panel);
        _work = BuildWork(); body.Children.Add(_work);
        body.Children.Add(BuildPanelEdge());
        // In full screen the terminal area spans the body and the rail and the panel lie over its edge (PlaceWork), so they are
        // drawn above it, on the base's backing, with the panel's resize grip above them all; in their own columns the order
        // changes nothing. The backing is collapsed in a window (MainWindow.FullScreen.cs).
        _sidebarBacking.Background = _theme.Base;
        Grid.SetColumnSpan(_sidebarBacking, 2); body.Children.Add(_sidebarBacking);
        Canvas.SetZIndex(_sidebarBacking, 1); Canvas.SetZIndex(_rail, 2); Canvas.SetZIndex(panel, 2); Canvas.SetZIndex(_panelEdge, 3);
        _root.Children.Add(body);
        // The unseen emoji box, for the Emoji… item on a folder's menu in either panel (docs/decisions/folder-emoji.md): under the root, so it is there whichever panel shows.
        _root.Children.Add(BuildFolderEmojiBox());
        var status = BuildStatusBar(); Grid.SetRow(status, 1); _root.Children.Add(status);
        // The zoomed pane's card follows the terminal area as it resizes (MainWindow.Zoom.cs).
        _root.LayoutUpdated += (_, _) => FollowZoomFrame();
        // A move or resize may put the window on another display, or at another scaling: the Screen frame reads the work area again.
        RegisterShortcuts();
        ApplySidebarSide();
        ApplyTabBarSide();
        var panelOpen = _preferences.PanelOpen;
        // A saved "workplaces", the panel's name until November 8, 2026, opens Folders.
        if (_preferences.ActivePanel == "workplaces") _preferences.ActivePanel = "folders";
        OpenPanel(_preferences.ActivePanel is "folders" or "workspaces" or "settings" ? _preferences.ActivePanel : "sessions", persist: false, animate: false);
        _ = SetPanelOpen(panelOpen, persist: false, animate: false);
    }

    /// <summary>
    /// Gives the taskbar button and the title bar the .ico frames at exactly their sizes: the button 24 px at 100% scaling
    /// (16 px on a small taskbar), the title bar the window's small-icon size, which Alt+Tab and the taskbar's thumbnail
    /// show too. AppWindow.SetIcon(path) hands both the 32 px frame, which Windows shrinks and softens: measured against a
    /// screenshot on September 29, 2026, and with Icon Branding Runway on September 30. SetIcon runs once, before these:
    /// called before them on every change, it left the button showing a scaled frame (Icon Branding Runway, September 30).
    /// Called again when the taskbar or the window may have changed scaling, and with <paramref name="force"/> when the app icon
    /// is another file (Settings → App icon), so the frames are reloaded at sizes that have not changed.
    /// </summary>
    private void SetShellIcons(string iconPath, bool force = false)
    {
        var size = ShellIcons.TaskbarButtonSize();
        if ((force || size != _taskbarIconSize) && ShellIcons.Load(iconPath, size) is var icon && icon != 0)
        {
            AppWindow.SetTaskbarIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(icon));
            ShellIcons.Destroy(_taskbarIcon);
            _taskbarIcon = icon; _taskbarIconSize = size;
        }
        var small = ShellIcons.SmallIconSize(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if ((force || small != _titleBarIconSize) && ShellIcons.Load(iconPath, small) is var smallIcon && smallIcon != 0)
        {
            AppWindow.SetTitleBarIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(smallIcon));
            ShellIcons.Destroy(_titleBarIcon);
            _titleBarIcon = smallIcon; _titleBarIconSize = small;
        }
    }

    /// <summary>
    /// Keeps the tab strip and the panel header clear of the caption buttons, which overlay the top-right corner once content
    /// extends into the title bar: the end of the strip with the rail on the left; with it on the right, the end of the panel
    /// header, and the end of the strip as far as a closing panel uncovers it (<see cref="PanelLayout.CaptionReserve"/>). With the
    /// bars at the bottom Windows draws no buttons (<see cref="ApplyCaptionButtons"/>), and nothing is kept clear.
    /// </summary>
    private void UpdateCaptionInset()
    {
        var scale = _root.XamlRoot?.RasterizationScale ?? 1;
        var slot = _panel.Visibility == Visibility.Visible ? _panel.ActualWidth : 0;
        // Full screen has no caption buttons either: Windows' presenter draws none.
        // A rail put away by Autohide sidebar covers none of the buttons, so the strip keeps clear of all of them.
        var (strip, header) = PanelLayout.CaptionReserve(SidebarRight, TabsBottom || _fullScreen ? 0 : AppWindow.TitleBar.RightInset / scale, SidebarOverlays ? 0 : RailWidth, slot);
        _captionSpacer.Width = strip;
        _panelHeader.Padding = SidebarRight ? new Thickness(6, 0, header + 8, 0) : new Thickness(16, 0, 6, 0);
    }

    /// <summary>
    /// Puts the rail and its panel on the side Settings names and the terminal area on the other, and mirrors what faces the
    /// terminals: the panel's resize edge and border, the edge the body slides under the rail from, and the Hide button and its
    /// arrow; the rail's selection pill stays on the window's edge. On the right the window's close button covers the rail's top,
    /// so the mark moves to the head of the tab strip (<see cref="PlaceMark"/>), and the minimize and maximize buttons cover the
    /// panel header's end, so the header takes the rail's color (<see cref="PaintPanelHeader"/>).
    /// </summary>
    private void ApplySidebarSide()
    {
        var right = SidebarRight;
        _leadColumn.Width = right ? new GridLength(1, GridUnitType.Star) : new GridLength(RailWidth);
        _trailColumn.Width = right ? new GridLength(RailWidth) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(_rail, right ? 2 : 0);
        Grid.SetColumn(_sidebarBacking, right ? 1 : 0);
        PlaceWork();
        _panelBody.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _panelBody.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        // Before the first layout the slot's width is only the one it was given.
        PinPanelBody(_panel.ActualWidth > 0 ? _panel.ActualWidth : _panel.Width);
        _panelEdge.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _panelEdge.Margin = right ? new Thickness(-PaneSplitter.GrabSize / 2, _panelEdge.Margin.Top, 0, _panelEdge.Margin.Bottom) : new Thickness(0, _panelEdge.Margin.Top, -PaneSplitter.GrabSize / 2, _panelEdge.Margin.Bottom);
        _panelEdge.Reversed = right;
        _panelHeader.ColumnDefinitions[0].Width = right ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _panelHeader.ColumnDefinitions[1].Width = right ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetColumn(_panelHide, right ? 0 : 1);
        Grid.SetColumn(_panelTitle, right ? 1 : 0);
        _panelTitle.Margin = new Thickness(right ? 8 : 0, 0, 0, 0);
        PaintPanelHeader();
        if (_panelHide.Content is FontIcon hide) { hide.Glyph = right ? "" : ""; }
        PlacePanelFoot();
        var placement = right ? Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Left : Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Right;
        foreach (var (button, pill) in _railItems.Values)
        {
            pill.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            ToolTipService.SetPlacement(button, placement);
        }
        foreach (var frame in _railFrames.Values) ToolTipService.SetPlacement(frame, placement);
        PlaceMark();
        UpdateCaptionInset();
    }

    /// <summary>
    /// The mark goes to the window's top-left corner: atop the rail with the rail on the left; with it on the right, at the head
    /// of the tab strip, since Windows' close button covers the rail's top. With the bars at the bottom Windows draws no buttons,
    /// and the mark stays atop the rail on either side. The head holds nothing else: the first tab starts at the rail's edge, or
    /// at the mark's, as far from the mark as the mark is from the window's edge (<see cref="FlipStripTemplate"/> takes out what
    /// the list lays before its first tab). Until October 15, 2026 the head kept an 18 px inset before the first tab, a drag
    /// area of the window's.
    /// </summary>
    private void PlaceMark()
    {
        var moved = SidebarRight && !TabsBottom;
        _railMark.Visibility = moved ? Visibility.Collapsed : Visibility.Visible;
        _stripMark.Visibility = moved ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// With the rail on the right Windows' minimize and maximize buttons cover the panel header's end, so the header takes the
    /// rail's color and ink: one title band under buttons drawn in the rail's ink, which a light panel under them (SIC light's)
    /// would leave unreadable. With the bars at the bottom there are no such buttons, and the header keeps the panel's.
    /// </summary>
    private void PaintPanelHeader()
    {
        var band = SidebarRight && !TabsBottom;
        _panelHeader.Background = band ? _theme.Rail : _theme.Panel;
        _panelTitle.Foreground = band ? _theme.StripText : _theme.Text;
        if (_panelHide.Content is FontIcon hide) hide.Foreground = band ? _theme.StripTextSecondary : _theme.TextSecondary;
    }

    /// <summary>
    /// Puts the bars along the top or along the bottom (Settings → Tabs). At the bottom the tab bar runs above the status bar
    /// and turns over with it everything that faces the panes: the strip and the content trade rows in the TabView's template
    /// (<see cref="FlipStripTemplate"/>), each tab is drawn upside down (<see cref="MirrorTab"/>), each pane's header goes under
    /// its terminal (<see cref="SessionPane.HeaderAtBottom"/>), and the keyboard's way from the panes to the strip becomes
    /// Alt+Down from the bottom row (<see cref="StripSide"/>). There is no title bar: Windows draws no buttons, SIC's own sit at
    /// the strip's end (<see cref="ApplyCaptionButtons"/>), and the window drags from the strip and the rail, so the panes reach
    /// the window's top edge. The notices stay above the status bar, so one showing lifts the strip with it.
    /// docs/decisions/tab-bar-side.md has the reasoning.
    /// </summary>
    private void ApplyTabBarSide()
    {
        var bottom = TabsBottom;
        FlipStripTemplate();
        // A rail lying over the terminals keeps clear of the strip on its new edge.
        PlaceWork();
        foreach (var host in _open.Values) MirrorTab(host);
        _stripBackdrop.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        // The notice's row keeps the 6 px under a closed notice, which at the bottom lies between the strip and the status bar:
        // in the rail's color the two read as one band rather than two split by a line of the window's base.
        _noticeHost.Background = bottom ? _theme.Rail : _theme.Frosted ? _theme.Base : null;
        _welcome.Margin = new Thickness(0, bottom ? 0 : StripHeight, 0, bottom ? BottomStripHeight : 0);
        _welcomeHost.Margin = _welcome.Margin;
        _workBacking.Margin = _welcome.Margin;
        ApplyCaptionButtons();
        PlaceMark();
        PaintPanelHeader();
        foreach (var host in _open.Values)
            foreach (var pane in host.Panes) { pane.Neighbors = AltArrowSides(host.Content.Tree, pane.Descriptor.Id); pane.HeaderAtBottom = bottom; }
        UpdateCaptionInset();
    }

    /// <summary>
    /// Windows draws the minimize, maximize and close buttons over the window's top-right corner while the bars are at the top.
    /// With them at the bottom it draws none (TitleBarHeightOption.Collapsed), and SIC's own take their place at the strip's end
    /// (MainWindow.WindowButtons.cs); in full screen it draws none either, and only Leave full screen stands there. Until content
    /// extends into the title bar there are no buttons to hide; the constructor calls this again once it does.
    /// </summary>
    private void ApplyCaptionButtons()
    {
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar.ExtendsContentIntoTitleBar)
        {
            AppWindow.TitleBar.PreferredHeightOption = TabsBottom ? Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed : Microsoft.UI.Windowing.TitleBarHeightOption.Standard;
            // The buttons' width is read again once Windows has laid them out, in case it reports them a turn late.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateCaptionInset);
        }
        PlaceWindowButtons();
    }

    /// <summary>
    /// The TabView template's part of the change: the strip's row and the content's trade places, and what the template lays
    /// along the strip's edge towards the panes goes to its other edge: the border line under the tabs (in three parts, the
    /// template's own and its list's) and the overflow scroll buttons. The 8 px the template leaves above the tabs, the
    /// TabView's padding, is dropped, with the strip's footer cut to the tabs' height: down there it is no title bar to drag by.
    /// On either side it closes up the strip's head, so the first tab starts where the head ends. Until the template is applied
    /// it waits for the TabView's Loaded, which calls it again.
    /// </summary>
    private void FlipStripTemplate()
    {
        var bottom = TabsBottom;
        // The template's own padding is 8 px above the tabs (TabViewHeaderPadding); SIC keeps half of it, the tabs taking the rest.
        if (bottom) _tabs.Padding = new Thickness(0);
        else _tabs.Padding = new Thickness(0, StripPadding, 0, 0);
        _stripFooter.Height = bottom ? BottomStripHeight : StripHeight;
        if (FindNamed<Grid>(_tabs, "TabContainerGrid") is not { } strip || VisualTreeHelper.GetParent(strip) is not Grid frame || frame.RowDefinitions.Count != 2
            || frame.Children.OfType<ContentPresenter>().FirstOrDefault(child => child.Name == "TabContentPresenter") is not { } content) return;
        // The first tab starts where the head ends (PlaceMark): the template keeps the head's column at least 2 px wide, the
        // list's scroll viewer keeps 2 px for its overflow button's column while there is no overflow, and the list lays 4 px
        // before its first tab, its items presenter's header, room for the selected tab's feet, which SIC cuts off (TrimTabFeet).
        // The 1 px the scroll viewer pads its content by stays, so the selected tab's fill, 1 px wider than the tab, starts
        // exactly at the head's end.
        if (strip.ColumnDefinitions.Count > 0) strip.ColumnDefinitions[0].MinWidth = 0;
        foreach (var presenter in AllNamed(strip, "ScrollContentPresenter"))
            if (VisualTreeHelper.GetParent(presenter) is Grid { ColumnDefinitions.Count: > 0 } viewer) viewer.ColumnDefinitions[0].MinWidth = 0;
        foreach (var line in AllNamed(strip, "LeftBottomBorderLine"))
            if (VisualTreeHelper.GetParent(line) is Grid { Width: 4.0 } lead) lead.Width = 0;
        frame.RowDefinitions[0].Height = bottom ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        frame.RowDefinitions[1].Height = bottom ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(strip, bottom ? 1 : 0);
        Grid.SetRow(content, bottom ? 0 : 1);
        foreach (var edge in AllNamed(strip, "LeftBottomBorderLine", "RightBottomBorderLine", "ScrollDecreaseButtonContainer", "ScrollIncreaseButtonContainer"))
            edge.VerticalAlignment = bottom ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    /// <summary>
    /// Keeps the tabs clear of the strip's footer. TabView (SizeToContent) gives its tab list the room its own width leaves after
    /// the head and the footer, but works it out only when its width or its tabs change, takes the footer's width from its
    /// desired size, which is no more than the room the footer was last given, and stops working it out at all once the strip
    /// is narrower than the head and the footer together. So a footer that grew while squeezed, as leaving full screen brings
    /// the caption buttons' spacer back where Leave full screen stood, kept the room it had had, and the tabs' scroll arrow ran
    /// under the minimize button. After each layout this works the room out again from the footer's own parts
    /// (<see cref="StripFooterWidth"/>) and the + button's column (the strip's third, since the button came back on November 8,
    /// 2026), and sets the list's width, its scroll arrows and their enabled state wherever they differ.
    /// </summary>
    private void FitTabList(object? sender, object args)
    {
        if (_tabs.ActualWidth <= 0) return;
        // The template's parts, found once: the layout passes this follows come many times a second while a terminal draws.
        if (_tabListParts is null)
        {
            if (FindNamed<Grid>(_tabs, "TabContainerGrid") is not { ColumnDefinitions.Count: 4 } found
                || FindNamed<ListView>(found, "TabListView") is not { } foundList || FindNamed<ItemsPresenter>(foundList, "TabsItemsPresenter") is not { } foundItems) return;
            _tabListParts = (found, foundList, foundItems);
        }
        var (strip, list, items) = _tabListParts.Value;
        var footer = StripFooterWidth();
        // The workspace indicator at the head gives way when the strip is too narrow for it and some tabs besides (a narrow window,
        // the sidebar on the right with its mark in the head): hidden, and shown again, by the room the strip would have without it,
        // so the answer is the same either way and the indicator does not flicker. The layout runs again with it changed.
        if (_workspaceChip is not null)
        {
            if (_workspaceChip.Visibility == Visibility.Visible && _workspaceChip.ActualWidth > 0) _chipWidth = _workspaceChip.ActualWidth + _workspaceChip.Margin.Left + _workspaceChip.Margin.Right;
            var shown = _workspaceChip.Visibility == Visibility.Visible ? _chipWidth : 0;
            var cramped = _tabs.ActualWidth - (strip.ColumnDefinitions[0].ActualWidth - shown) - strip.ColumnDefinitions[2].ActualWidth - footer < _chipWidth + MinTabRoom;
            if (cramped != _chipCramped) { _chipCramped = cramped; RefreshWorkspaceChip(); return; }
        }
        var room = Math.Max(0, _tabs.ActualWidth - strip.ColumnDefinitions[0].ActualWidth - strip.ColumnDefinitions[2].ActualWidth - footer);
        var overflow = items.ActualWidth > room;
        var visibility = overflow ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        var column = strip.ColumnDefinitions[1];
        if (Math.Abs(column.MaxWidth - room) < 0.5 && Math.Abs(list.MaxWidth - room) < 0.5 && ScrollViewer.GetHorizontalScrollBarVisibility(list) == visibility
            && Math.Abs(strip.ColumnDefinitions[3].MinWidth - footer) < 0.5) return;
        column.MaxWidth = room;
        list.MaxWidth = room;
        strip.ColumnDefinitions[3].MinWidth = footer;
        if (ScrollViewer.GetHorizontalScrollBarVisibility(list) == visibility) return;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, visibility);
        // TabView sets the arrows' state as the list scrolls; newly shown, they start where the list is: more to the right.
        if (overflow && FindNamed<ScrollViewer>(list, "ScrollViewer") is { } viewer)
        {
            if (FindNamed<Microsoft.UI.Xaml.Controls.Primitives.RepeatButton>(viewer, "ScrollDecreaseButton") is { } decrease) decrease.IsEnabled = viewer.HorizontalOffset > 0.1;
            if (FindNamed<Microsoft.UI.Xaml.Controls.Primitives.RepeatButton>(viewer, "ScrollIncreaseButton") is { } increase) increase.IsEnabled = true;
        }
    }

    /// <summary>
    /// The footer's own width, whatever room it was last given: the drag area's least, the caption buttons' spacer, and SIC's
    /// window buttons that show. Read after every layout pass of the window (<see cref="FitTabList"/>), so it allocates nothing.
    /// </summary>
    private double StripFooterWidth()
    {
        var width = _titleBar.MinWidth + _captionSpacer.Width;
        if (_windowButtons.Visibility != Visibility.Visible) return width;
        var buttons = _windowButtons.Children;
        for (var index = 0; index < buttons.Count; index++)
            if (buttons[index] is Button { Visibility: Visibility.Visible } button) width += button.Width;
        return width;
    }

    /// <summary>Every element under <paramref name="parent"/> with one of <paramref name="names"/>: template parts that share a name across templates.</summary>
    private static IEnumerable<FrameworkElement> AllNamed(DependencyObject parent, params string[] names)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && names.Contains(element.Name)) yield return element;
            foreach (var found in AllNamed(child, names)) yield return found;
        }
    }

    private FrameworkElement BuildRail()
    {
        _rail.Background = _theme.Rail;
        _rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StripHeight) });
        _rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // The mark's row drags the window like the rest of the rail, and a drag area gets no hover, so the name it once
        // showed as a tooltip is left to screen readers; the welcome page and About spell it out.
        var mark = _railMark = new Border { Height = StripHeight, Child = SicMark.Create(24, () => _theme.StripSurface) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mark, "SicTerm");
        _rail.Children.Add(mark);
        var top = new StackPanel { Spacing = 2, Padding = new Thickness(4, 2, 4, 4) };
        // Workspaces, Folders, then Sessions (docs/decisions/workspaces.md): what is open, where sessions start, what was said;
        // Workspaces leads, being where the work is. Its badge shows while an agent in a workspace out of view wants the user (RefreshWorkspaceBadge).
        var workspaces = RailButton("workspaces", WorkspaceGlyph, "Agent Workspaces", () => TogglePanel("workspaces"));
        _workspaceBadge = new Border
        {
            Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = _theme.WarningDot, BorderBrush = _theme.Rail, BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        ((Grid)workspaces.Content).Children.Add(_workspaceBadge);
        top.Children.Add(workspaces);
        top.Children.Add(RailButton("folders", FolderGlyph, "Folders", () => TogglePanel("folders")));
        top.Children.Add(RailButton("sessions", SearchGlyph, "Sessions (Ctrl+Shift+K)", () => TogglePanel("sessions")));
        Grid.SetRow(top, 1); _rail.Children.Add(top);
        var bottom = new StackPanel { Spacing = 2, Padding = new Thickness(4, 4, 4, 6) };
        bottom.Children.Add(RailButton("new", "\uE710", "New session (Ctrl+Shift+T)", async () => await ShowNewSessionAsync()));
        // Lit whenever a pane has something to resume (a restored placeholder, a conversation opened as a tab); see UpdateStatus.
        // Its glyph is the mark in one colour rather than a generic play, since resuming everything is what SIC is for, and a
        // size up from the 16 px Segoe glyphs round it, since it carries more detail.
        var resumeGlyph = ResumeAllMark.Create(_theme.RailText, 24);
        _resumeGlyph = (Shape)resumeGlyph.Child;
        bottom.Children.Add(RailFrame(RailButton("resume", resumeGlyph, "Resume all (F5)", async () => await ResumeAllFromUserAsync(new("the rail's Resume all button", DateTimeOffset.UtcNow)))));
        // Consolidate tabs left the rail on November 8, 2026: it is on the workspace's menu and F6, since workspaces keep the strip short.
        // No light / dark toggle here since October 8, 2026: a theme is chosen in Settings \u2192 Appearance, and the rail is for actions.
        bottom.Children.Add(RailButton("settings", "\uE713", "Settings", () => TogglePanel("settings")));
        Grid.SetRow(bottom, 3); _rail.Children.Add(bottom);
        return _rail;
    }

    private Button RailButton(string id, string glyph, string tooltip, Action action) => RailButton(id, Theme.Icon(glyph, 16, _theme.RailText), tooltip, action);

    private Button RailButton(string id, UIElement icon, string tooltip, Action action)
    {
        var content = new Grid { Width = 40, Height = 40 };
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = _theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        content.Children.Add(pill);
        content.Children.Add(icon);
        var button = new Button
        {
            Content = content, Padding = new Thickness(0), Width = 40, Height = 40, CornerRadius = new CornerRadius(5),
            Background = _theme.Transparent, BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, tooltip);
        ToolTipService.SetPlacement(button, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Right);
        button.Click += (_, _) => action();
        _railItems[id] = (button, pill);
        return button;
    }

    // A disabled control takes no pointer input, so a rail button that is greyed while it has nothing to do would show no
    // tooltip. Such a button sits in a transparent frame, which takes the tooltip while the button is disabled (SetRailButton),
    // so "Nothing to resume (F5)" still names the button and its key.
    private Border RailFrame(Button button)
    {
        var frame = new Border { Child = button, Background = _theme.Transparent };
        _railFrames[_railItems.First(item => item.Value.Button == button).Key] = frame;
        ToolTipService.SetPlacement(frame, ToolTipService.GetPlacement(button));
        return frame;
    }

    /// <summary>Enables or disables a framed rail button (<see cref="RailFrame"/>), with its tooltip on whichever of the two
    /// takes pointer input: the button while enabled, its frame while not. A tooltip already shown is left alone.</summary>
    private void SetRailButton(string id, bool enabled, string tooltip)
    {
        var button = _railItems[id].Button;
        button.IsEnabled = enabled;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        foreach (var (owner, shows) in new (DependencyObject, bool)[] { (button, enabled), (_railFrames[id], !enabled) })
            if (ToolTipService.GetToolTip(owner) as string != (shows ? tooltip : null)) ToolTipService.SetToolTip(owner, shows ? tooltip : null);
    }

    private Grid _panelFoot = null!;
    private Button _panelFootHide = null!;
    private Grid? _settingsBar;

    /// <summary>What a panel's scrolling content keeps clear at its end, so its last row can be brought out from under Hide panel.</summary>
    internal const double PanelFootBuffer = 36;

    /// <summary>
    /// Hide panel at the foot of every panel (Chris, November 13 and 14, 2026; docs/decisions/sidebar-side.md): the header's
    /// button again, drawn over the content's bottom corner towards the terminals, level with the rail's Settings button. In
    /// Settings it stands directly above the bar's button at that end, Quit or General, so the bar keeps the panel's foot. It
    /// is as far from the panel's edge as the header's button, so the two are one above the other in every panel (a grid of
    /// the bar's columns centred it over Quit until November 14, 2026). Each panel's content keeps <see cref="PanelFootBuffer"/> clear at its end.
    /// </summary>
    private Grid BuildPanelFoot()
    {
        _panelFootHide = _theme.IconButton("", "Hide panel (Ctrl+Shift+S)", 32, 14, _theme.TextSecondary);
        _panelFootHide.Background = _theme.Panel; _panelFootHide.HorizontalAlignment = HorizontalAlignment.Right;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_panelFootHide, "Hide panel");
        _panelFootHide.Click += (_, _) => _ = SetPanelOpen(false);
        _panelFoot = new Grid { Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Bottom };
        _panelFoot.Children.Add(_panelFootHide);
        return _panelFoot;
    }

    /// <summary>Hide panel's place: the end towards the terminals, above Settings' bar when Settings is the panel shown.</summary>
    private void PlacePanelFoot()
    {
        if (_panelFoot is null) return;
        _panelFootHide.HorizontalAlignment = SidebarRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        if (_panelFootHide.Content is FontIcon icon) icon.Glyph = SidebarRight ? "" : "";
        var bar = _preferences.ActivePanel == "settings" && _settingsBar is { ActualHeight: > 0 } shown ? shown.ActualHeight : 0;
        _panelFoot.Margin = new Thickness(0, 0, 0, bar > 0 ? bar + 4 : 10);
    }

    private FrameworkElement BuildPanel()
    {
        _panelBody.BorderBrush = _theme.Stroke;
        _panelBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _panelBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // The fill is the rows', not the body's: with the rail on the right the header joins the title band and takes the rail's
        // color over the window's base, as the tab strip beside it does, which it could not over a panel fill.
                var surface = new Border { Background = _theme.Panel };
        Grid.SetRow(surface, 1); _panelBody.Children.Add(surface);
        // Its columns, padding and colors follow the side the rail is on (ApplySidebarSide, UpdateCaptionInset).
        var header = _panelHeader = new Grid { Height = StripHeight };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition());
        _panelTitle = _theme.Heading("Sessions", 14); _displayText.Add(_panelTitle);
        header.Children.Add(_panelTitle);
        var close = _panelHide = _theme.IconButton("\uE89F", "Hide panel (Ctrl+Shift+S)", 32, 14);
        close.Click += (_, _) => _ = SetPanelOpen(false);
        header.Children.Add(close);
        _panelBody.Children.Add(header);
        Grid.SetRow(_panelContent, 1); _panelBody.Children.Add(_panelContent);
        Grid.SetRow(BuildPanelFoot(), 1); _panelBody.Children.Add(_panelFoot);
        _panels["folders"] = ("Folders", BuildFoldersPanel());
        _panels["workspaces"] = ("Agent Workspaces", BuildWorkspacesPanel());
        _panels["sessions"] = ("Sessions", BuildSessionsPanel());
        _panels["settings"] = ("Settings", BuildSettingsPanel());
        // The body keeps its full width whatever the slot's, so nothing reflows mid-slide: it is pinned to the slot's edge
        // towards the terminals (PinPanelBody), so it moves under the rail as one piece, and the slot clips it, since panels do not clip.
        _panelBody.Width = _panelWidth;
        _panel.Width = _panelWidth;
        _panel.Children.Add(_panelBody);
        _panel.SizeChanged += (_, args) =>
        {
            PinPanelBody(args.NewSize.Width);
            (_panel.Clip ??= new RectangleGeometry()).Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
            UpdateCaptionInset();
        };
        return _panel;
    }

    /// <summary>
    /// Pins the full-width body to the slot's edge towards the terminals: its right edge, by a negative left margin, with the rail
    /// on the left; its left edge with the rail on the right, where the body is left-aligned and its overflow falls under the rail.
    /// </summary>
    private void PinPanelBody(double slot) => _panelBody.Margin = SidebarRight ? new Thickness(0) : new Thickness(slot - _panelBody.Width, 0, 0, 0);

    /// <summary>
    /// The panel's edge towards the terminals as a drag handle, on whichever side the rail leaves it (ApplySidebarSide). The
    /// body's 1 px border stays the visible line; the 8 px grab area straddles it, half over the terminal area, so the session
    /// list's scroll bar at the panel's right edge keeps its own pointer.
    /// </summary>
    private FrameworkElement BuildPanelEdge()
    {
        _panelEdge = new PaneSplitter(rows: false, () => _panelBody.ActualWidth, ResizePanel, _theme, line: false);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_panelEdge, "Panel width: drag to resize, double-click to reset");
        _panelEdge.Released += () => SetPanelWidth((int)Math.Round(_panelBody.Width));
        _panelEdge.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler((_, args) => { args.Handled = true; SetPanelWidth(PanelLayout.DefaultWidth); }), handledEventsToo: true);
        Grid.SetColumn(_panelEdge, 1);
        return _panelEdge;
    }

    /// <summary>
    /// Resizes the open panel live, body and slot together so nothing slides, within <see cref="PanelLayout.ClampWidth"/>. The
    /// drag reads the laid-out width, as the pane dividers do, because the thumb reports each step against where it was last laid out.
    /// </summary>
    private void ResizePanel(double requested)
    {
        // Before the first layout the window's width is unknown, and only the panel's own bounds apply.
        var shared = _root.ActualWidth > 0 ? _root.ActualWidth - RailWidth : double.PositiveInfinity;
        _panelWidth = PanelLayout.ClampWidth((int)Math.Round(requested), shared);
        _panelBody.Width = _panelWidth;
        if (_panelOpen && _panelMotion is null) _panel.Width = _panelWidth;
    }

    /// <summary>Sets the panel's width, clamped, and saves it: the end of a drag, a double-click reset, and the smoke.</summary>
    internal int SetPanelWidth(int width)
    {
        ResizePanel(width);
        _preferences.PanelWidth = (int)_panelWidth;
        SavePreferences();
        return _preferences.PanelWidth;
    }

    private Grid BuildWork()
    {
        var work = new Grid();
        work.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        work.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // With a frosted or clear chrome, the window has no base of its own, so the terminal area gets one: the gaps between panes and the welcome page stay solid.
        _workBacking.Margin = new Thickness(0, StripHeight, 0, 0);
        work.Children.Add(_workBacking);
        _stripBackdrop.Background = _theme.Rail;
        work.Children.Add(_stripBackdrop);
        // The strip's head: the mark with the sidebar on the right, and nothing otherwise, so the first tab starts at the rail's
        // edge (PlaceMark). It held the running count until October 9, 2026, and an inset before the first tab until October 15;
        // the status bar has the count, and the strip keeps its width for tabs. With the mark in it, it is a drag area
        // (MainWindow.DragRegions.cs reads its bounds).
        var header = _stripHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch, MinHeight = TabContentHeight };
        // Centred in a rail's width, as it is at the top of the rail; a drag area, so it has no tooltip.
        _stripMark = new Border { Width = RailWidth, Child = SicMark.Create(24, () => _theme.StripSurface), Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_stripMark, "SicTerm");
        header.Children.Add(_stripMark);
        // The current workspace's name, while there is a listed folder or more than one workspace (MainWindow.Workspaces.cs); the head is a drag area less this indicator.
        header.Children.Add(BuildWorkspaceChip());
        _tabs.TabStripHeader = header;
        // Nothing but a drag area and the caption-button spacer, or SIC's own window buttons with the bars at the bottom: split
        // and find live in each pane's header, the tab commands on each tab's right-click menu and New session on the rail and
        // on the strip's own + after the last tab (back since November 8, 2026, at Chris's request), so the footer keeps its
        // width for the drag area.
        var footer = _stripFooter;
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The empty strip after the tabs is a drag area; its bounds are what the drag regions read. With the strip full it is
        // 8 px, which with the scroll arrow's own 8 px keeps a small gap before the window's buttons; the rail drags the window
        // too. It was 48 px until October 16, 2026.
        _titleBar.Background = _theme.Transparent;
        _titleBar.MinWidth = 8;
        footer.Children.Add(_titleBar);
        Grid.SetColumn(_captionSpacer, 1); footer.Children.Add(_captionSpacer);
        BuildWindowButtons();
        Grid.SetColumn(_windowButtons, 1); footer.Children.Add(_windowButtons);
        _tabs.TabStripFooter = footer;
        // The + after the last tab: New session, as the rail's button, in the folder the focused pane works in.
        _tabs.AddTabButtonClick += async (_, _) => await ShowNewSessionAsync();
        _tabs.Loaded += (_, _) => { if (FindNamed<Button>(_tabs, "AddButton") is { } addButton) ToolTipService.SetToolTip(addButton, "New session (Ctrl+Shift+T)"); };
        _theme.StyleTabStrip(_tabs.Resources);
        _tabs.Loaded += FitStripBackdrop;
        _tabs.Loaded += (_, _) => FlipStripTemplate();
        _tabs.LayoutUpdated += FitTabList;
        _tabs.TabCloseRequested += async (_, args) => await CloseTabAsync(args.Tab);
        _tabs.SelectionChanged += (_, _) => OnTabSelected();
        WatchWindowInView();
        WatchTabDrags();
        WatchTabDrops();
        // On the tab strip (reached with Alt+Up from a pane's top row), Down and Enter go into the focused tab and Esc back into
        // the one showing; with the tabs at the bottom (reached with Alt+Down from the bottom row), Up takes Down's place. With
        // Alt held the window's Alt+Arrow keys have it. Enter is taken even though a tab selects on it by itself, since selecting
        // alone would leave the keyboard on the strip.
        _tabs.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.OriginalSource is not TabViewItem focused || AltHeld) return;
            var into = args.Key == VirtualKey.Enter || args.Key == (TabsBottom ? VirtualKey.Up : VirtualKey.Down) ? focused
                : args.Key == VirtualKey.Escape ? _tabs.SelectedItem as TabViewItem : null;
            if (into is not null && EnterTab(into)) args.Handled = true;
        }), handledEventsToo: true);
        work.Children.Add(_tabs);
        _welcome = BuildWelcome();
        work.Children.Add(_welcome);
        // The first-run film and its card lie over the tab area, welcome page or tabs (MainWindow.Welcome.cs).
        _welcomeHost.Margin = _welcome.Margin;
        work.Children.Add(_welcomeHost);
        work.Children.Add(BuildStripOverlay());
        WatchPaneDrags();
        WatchFolderDrags();
        _notice.RenderTransform = _noticeSlide;
        _notice.Closed += (_, _) => RestNotice();
        _noticeHost.Children.Add(_notice);
        _noticeHost.SizeChanged += (_, changed) => (_noticeHost.Clip ??= new RectangleGeometry()).Rect = new Windows.Foundation.Rect(0, 0, changed.NewSize.Width, changed.NewSize.Height);
        Grid.SetRow(_noticeHost, 1); work.Children.Add(_noticeHost);
        return work;
    }

    /// <summary>
    /// StripHeight is the strip the tab sizes add up to, but the TabView template has the last word. The rail backdrop
    /// follows the strip's real height, or a band of the base color shows under every tab and the active tab no longer
    /// reads as the only one that meets its pane.
    /// </summary>
    private void FitStripBackdrop(object sender, RoutedEventArgs args)
    {
        if (FindNamed<Grid>(_tabs, "TabContainerGrid") is not { } strip) return;
        _tabs.Loaded -= FitStripBackdrop;
        if (strip.ActualHeight > 0) _stripBackdrop.Height = strip.ActualHeight;
        strip.SizeChanged += (_, changed) => _stripBackdrop.Height = changed.NewSize.Height;
    }

    /// <summary>Shown in the tab area while no tab is open: the mark, the name, and the ways to start.</summary>
    private Grid BuildWelcome()
    {
        var host = new Grid { Margin = new Thickness(0, StripHeight, 0, 0), Visibility = Visibility.Collapsed };
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 480 };
        var mark = SicMark.Create(96, () => _theme.Base.Color); mark.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(mark);
        var name = SicMark.Wordmark(_theme, 40); name.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(name);
        // The tagline About carries, on two lines under the name (Chris, October 18, 2026).
        var tagline = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var line in new[] { "Super-Intelligent-Chat", "Terminal" })
            tagline.Children.Add(new TextBlock
            {
                Text = line, FontFamily = Theme.AzeretMono, FontSize = 16, Foreground = _theme.TextSecondary, TextAlignment = TextAlignment.Center,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, HorizontalAlignment = HorizontalAlignment.Center
            });
        stack.Children.Add(tagline);
        // No line of explanation under the name: the buttons say what to do (Chris, October 19, 2026; rotating tips may come later).
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        var create = _theme.PrimaryButton("New session", "Ctrl+Shift+T");
        create.Click += async (_, _) => await ShowNewSessionAsync();
        var sessions = Theme.IconTextButton(SearchGlyph, "Open Sessions");
        sessions.Click += (_, _) => OpenPanel("sessions");
        actions.Children.Add(create); actions.Children.Add(sessions);
        stack.Children.Add(actions);
        host.Children.Add(stack);
        return host;
    }

    private FrameworkElement BuildStatusBar()
    {
        var bar = new Grid { Height = 26, Padding = new Thickness(12, 0, 12, 0), BorderBrush = _theme.Stroke, BorderThickness = new Thickness(0, 1, 0, 0), Background = _theme.Rail };
        // The running count at the left; the history line takes what is left and trims; the usage limits at the right end
        // (MainWindow.Limits.cs). The ink is the rail text lifted to 4.5:1 on the bar (Theme.BarInk), for any theme file.
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _statusDot = Theme.Dot(_theme.Neutral);
        _statusText = _theme.Tertiary("");
        _statusText.Foreground = _theme.BarInk;
        left.Children.Add(_statusDot); left.Children.Add(_statusText);
        bar.Children.Add(left);
        _statusDetail = _theme.Tertiary("Local history · detecting tools");
        _statusDetail.Foreground = _theme.BarInk;
        _statusDetail.HorizontalAlignment = HorizontalAlignment.Right;
        _statusDetail.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_statusDetail, 1); bar.Children.Add(_statusDetail);
        var limits = BuildLimitsPanel();
        limits.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(limits, 2); bar.Children.Add(limits);
        bar.SizeChanged += (_, args) => FitStatusBar(args.NewSize.Width);
        return bar;
    }

    private void RegisterShortcuts()
    {
        // Auto placement turns an element's first accelerator into its tooltip, so hovering the terminal (no tooltip of
        // its own) popped "Ctrl+K" from the root and "Ctrl+F4" from TabView's built-in close-tab accelerator.
        _root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        _tabs.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        void Add(VirtualKey key, VirtualKeyModifiers modifiers, string name)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += async (_, args) => { args.Handled = true; await HandleShortcutAsync(name); };
            _root.KeyboardAccelerators.Add(accelerator);
        }
        Add(VirtualKey.K, VirtualKeyModifiers.Control, "search");
        // With Shift too, the one a terminal takes: there Ctrl+K alone is the program's own (kill to the end of the line), so the
        // page sends only Ctrl+Shift+K on. Both now work from anywhere else (Chris, November 12, 2026).
        Add(VirtualKey.K, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "search");
        Add(VirtualKey.T, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "new-tab");
        Add(VirtualKey.W, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "close-tab");
        Add(VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "duplicate-tab");
        Add(VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "resume-all");
        Add(VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "find");
        Add(VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "open-folder");
        Add(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "toggle-panel");
        Add(VirtualKey.A, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "workspace-list");
        // WezTerm's key for a pane zoomed over its tab, and back (MainWindow.Zoom.cs).
        Add(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "zoom");
        Add(VirtualKey.Tab, VirtualKeyModifiers.Control, "next-tab");
        Add(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "previous-tab");
        // VK_OEM_PLUS and VK_OEM_MINUS: Windows Terminal's own pair for a split into columns and into rows.
        Add((VirtualKey)0xBB, VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "split");
        Add((VirtualKey)0xBD, VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "split-down");
        Add(VirtualKey.F1, VirtualKeyModifiers.None, "help");
        Add(VirtualKey.F2, VirtualKeyModifiers.Control, "rename-tab");
        // F2 from anywhere but a terminal: the tab with the focus on the tab strip, else the pane or tab F2 names (RenameFocused).
        var rename = new KeyboardAccelerator { Key = VirtualKey.F2, Modifiers = VirtualKeyModifiers.None };
        rename.Invoked += (_, args) => { args.Handled = true; RenameFocused(); };
        _root.KeyboardAccelerators.Add(rename);
        Add(VirtualKey.F5, VirtualKeyModifiers.None, "resume-all");
        // Ctrl+F5: F5 for the current workspace alone (MainWindow.ResumeAll.cs).
        Add(VirtualKey.F5, VirtualKeyModifiers.Control, "resume-workspace");
        Add(VirtualKey.F6, VirtualKeyModifiers.None, "consolidate");
        Add(VirtualKey.F11, VirtualKeyModifiers.None, "full-screen");
        for (var digit = 1; digit <= 9; digit++)
        {
            Add(VirtualKey.Number0 + digit, VirtualKeyModifiers.Control, "tab-" + digit);
            // Alt+digit too, the modifier the pane keys use. The digit row only: Alt with the keypad types Alt codes.
            Add(VirtualKey.Number0 + digit, VirtualKeyModifiers.Menu, "tab-" + digit);
            // Ctrl+Alt+digit: the workspace at that place in the Workspaces panel, 9 for the last (docs/decisions/workspaces.md).
            Add(VirtualKey.Number0 + digit, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "workspace-" + digit);
        }
        // Ctrl+Alt+Left and Right step through the workspaces.
        Add(VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "previous-workspace");
        Add(VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "next-workspace");
        // Alt+Arrow, Windows Terminal's pane keys: the pane on that side, the tab strip above the top row, or on the strip the tab
        // beside; where there is none the key goes on to the control that has the focus (MoveFocus).
        foreach (var (key, direction) in new[] { (VirtualKey.Left, PaneDirection.Left), (VirtualKey.Right, PaneDirection.Right), (VirtualKey.Up, PaneDirection.Up), (VirtualKey.Down, PaneDirection.Down) })
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Menu };
            accelerator.Invoked += (_, args) => args.Handled = MoveFocus(direction);
            _root.KeyboardAccelerators.Add(accelerator);
        }
    }

    private void TogglePanel(string name)
    {
        if (_panelOpen && _preferences.ActivePanel == name) _ = SetPanelOpen(false);
        else OpenPanel(name);
    }

    private void OpenPanel(string name, bool persist = true, bool animate = true)
    {
        var (title, content) = _panels[name];
        _panelTitle.Text = title;
        // Settings restored at launch on General or Agents ended SIC a second or two after its window appeared, in
        // Microsoft.UI.Xaml.dll (0xC000027B, E_UNEXPECTED, no managed exception), as an Image in the title bar once did:
        // without the app-icon previews (General) or the agent tiles (Agents) it ran, and opened after launch it runs. The
        // single Settings page before the tabs (0.9.5) held the same images and did not crash; why was not found. So when
        // Settings is the panel restored at launch, its content joins once the window has loaded.
        if (name == "settings" && !_root.IsLoaded)
        {
            _panelContent.Content = null;
            void Attach(object sender, RoutedEventArgs args)
            {
                _root.Loaded -= Attach;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_preferences.ActivePanel == "settings" && _panelContent.Content is null) _panelContent.Content = content;
                });
            }
            _root.Loaded += Attach;
        }
        else _panelContent.Content = content;
        _preferences.ActivePanel = name;
        PlacePanelFoot();
        _ = SetPanelOpen(true, persist, animate);
    }

    /// <summary>
    /// Opens or closes the panel, sliding it unless asked not to (startup); the task ends when the panel is at rest. Only the user
    /// opens or closes it: Resume all and New session here closed it on their behalf until November 9, 2026, when the tabs spread
    /// over workspaces made the panel the way to the rest.
    /// </summary>
    private async Task SetPanelOpen(bool open, bool persist = true, bool animate = true)
    {
        _panelOpen = open;
        foreach (var (id, item) in _railItems)
            item.Pill.Visibility = open && id == _preferences.ActivePanel ? Visibility.Visible : Visibility.Collapsed;
        _preferences.PanelOpen = open;
        if (persist) SavePreferences();
        // Folders coming into view reads its open folder trees again, for a folder made since they were listed.
        if (open) RefreshOpenFolders();
        // In full screen a panel opening brings the sidebar out, held while the keyboard is in it (Ctrl+Shift+K, Ctrl+Shift+S); the
        // panel closing sends the rail away with it, once the panel has slid shut.
        // With Autohide sidebar on, in the window, a panel opening docks the rail in its column first, and the panel closing
        // puts the rail away once the panel has slid shut.
        if (_fullScreen && open) RevealSidebar(held: true, slide: false);
        else if (open && _preferences.SidebarAutoHide) DockSidebar();
        await SlidePanelAsync(open, animate && PanelSlides);
        if (_fullScreen && !open && !_panelOpen) HideSidebar();
        else if (!open && !_panelOpen) AutoHideSidebar(animate);
    }

    /// <summary>
    /// Animates the slot's width, so the terminal area beside it follows frame by frame. A slide interrupted part-way carries
    /// on from where it is, in the time the remaining distance would have taken.
    /// </summary>
    private async Task SlidePanelAsync(bool open, bool animate)
    {
        var target = open ? _panelWidth : 0;
        var from = _panel.Visibility == Visibility.Collapsed ? 0 : _panel.ActualWidth;
        StopPanelMotion();
        if (open) _panel.Visibility = _panelEdge.Visibility = Visibility.Visible;
        if (!animate || Math.Abs(target - from) < 0.5) { RestPanel(target); return; }
        _panel.Width = from;
        var whole = open ? PanelOpenDuration : PanelCloseDuration;
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, whole.TotalMilliseconds * Math.Abs(target - from) / _panelWidth));
        var animation = new DoubleAnimation
        {
            From = from, To = target, Duration = new Duration(duration), EnableDependentAnimation = true,
            EasingFunction = new ExponentialEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn, Exponent = open ? 5 : 4 }
        };
        Storyboard.SetTarget(animation, _panel);
        Storyboard.SetTargetProperty(animation, "Width");
        var motion = new Storyboard();
        motion.Children.Add(animation);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        motion.Completed += (_, _) => completed.TrySetResult();
        _panelMotion = motion;
        motion.Begin();
        // The compositor does not tick while the window is hidden; a slide that never completes must not leave the panel half-way.
        await Task.WhenAny(completed.Task, Task.Delay(duration + TimeSpan.FromSeconds(1)));
        if (!ReferenceEquals(_panelMotion, motion)) return;
        _panelMotion = null;
        motion.Stop();
        RestPanel(target);
    }

    private void StopPanelMotion()
    {
        if (_panelMotion is not { } motion) return;
        _panelMotion = null;
        motion.Stop();
    }

    /// <summary>The panel at rest: its width set outright, and collapsed when closed so it takes no input.</summary>
    private void RestPanel(double width)
    {
        _panel.Width = width;
        if (width != 0) return;
        _panel.Visibility = _panelEdge.Visibility = Visibility.Collapsed;
        // A collapsed slot reports no size change, and the strip may now reach under the caption buttons.
        UpdateCaptionInset();
    }

    private void FocusSearch()
    {
        OpenPanel("sessions");
        _search.Focus(FocusState.Programmatic);
    }

    private void ApplyTheme(string id)
    {
        var definition = ThemeCatalog.Find(id);
        // A theme change left the Settings panel scrolled to its end (reported October 1, 2026), so the panel goes back to where
        // the theme was chosen: once now, and once more after the re-layout the new theme brings has run.
        var settingsOffset = _settingsScroll is { } settings ? settings.VerticalOffset : (double?)null;
        _theme.Apply(definition);
        _preferences.Theme = definition.Id;
        ApplyThemeChrome();
        PaintTabs();
        // The indicator's tint is mixed over the strip, which the theme sets.
        RefreshWorkspaceChip();
        // The rows wear their folders' colors in the shade for the theme's surface.
        RefreshWorkspaceStates();
        PaintGroupHeaders();
        // A border in one of the theme's colors takes the new theme's shade, and the button's dot with it.
        RepaintPaneBorder();
        SavePreferences();
        // The terminals keep their own palette (ThemeCatalog.Terminal): recoloring under a running agent broke its interface.
        RefreshThemePicker();
        if (settingsOffset is { } offset)
        {
            _settingsScroll.ChangeView(null, offset, null, disableAnimation: true);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _settingsScroll.ChangeView(null, offset, null, disableAnimation: true));
        }
    }

    /// <summary>Everything that is not a live brush: the XAML theme, Mica versus opaque base, caption buttons, display fonts.</summary>
    private void ApplyThemeChrome()
    {
        var definition = _theme.Definition;
        _root.RequestedTheme = definition.Light ? ElementTheme.Light : ElementTheme.Dark;
        // With a frosted or clear chrome, the backdrop shows wherever nothing solid is painted: the root is clear, and the terminal area has its own base.
        _root.Background = definition.Mica || _theme.Frosted ? null : _theme.Base;
        _workBacking.Background = _theme.Frosted ? _theme.Base : null;
        // The 6 px the notice's row keeps under the terminals is the window's base too, not a slit onto the desktop.
        if (!TabsBottom) _noticeHost.Background = _theme.Frosted ? _theme.Base : null;
        _backdrop.Tint = _theme.Base.Color;
        foreach (var text in _displayText) text.FontFamily = _theme.Display;
        // The mark has a set for light surfaces and one for dark, and lays its faint tail over the surface: the rail, the
        // welcome page and About may have changed colour.
        SicMark.RedrawAll();
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = _theme.RailText.Color;
            bar.ButtonInactiveForegroundColor = Theme.Blend(_theme.RailText.Color, _theme.Rail.Color.A > 0 ? _theme.Rail.Color : _theme.Base.Color, 0.4);
            bar.ButtonHoverBackgroundColor = _theme.Hover.Color;
            bar.ButtonHoverForegroundColor = _theme.RailText.Color;
            bar.ButtonPressedBackgroundColor = _theme.Pressed.Color;
            bar.ButtonPressedForegroundColor = _theme.RailText.Color;
        }
        RefreshWindowButtons();
    }

    /// <summary>Brings the window back from the tray, restoring it if it was minimized rather than hidden.</summary>
    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
    }

    private TabHost? SelectedHost => _tabs.SelectedItem is TabViewItem item && _open.TryGetValue(item, out var host) ? host : null;
    private IEnumerable<SessionPane> SelectedPanes => SelectedHost?.Panes ?? [];
    private IEnumerable<SessionPane> AllPanes => _open.Values.SelectMany(host => host.Panes);

    /// <summary>
    /// Shows a notice that stays until its X is clicked: an error, a warning, anything with a button, and anything the
    /// reader would be worse off for missing. <paramref name="action"/> is its button, and a notice without one takes the
    /// last one away.
    /// </summary>
    private void Notify(InfoBarSeverity severity, string title, string message, Microsoft.UI.Xaml.Controls.Primitives.ButtonBase? action = null)
        => ShowNotice(severity, title, message, action, brief: false);

    /// <summary>
    /// Shows a brief notice: it slides up, holds a few seconds, and slides back down on its own. For a confirmation of
    /// something that plainly happened, or a request that had nothing to do, where missing it costs the reader nothing.
    /// </summary>
    private void NotifyBriefly(string title, string message) => ShowNotice(InfoBarSeverity.Informational, title, message, null, brief: true);

    private void ShowError(string text) => Notify(InfoBarSeverity.Error, "That did not work", text);

    private void ShowNotice(InfoBarSeverity severity, string title, string message, Microsoft.UI.Xaml.Controls.Primitives.ButtonBase? action, bool brief)
    {
        var generation = ++_noticeGeneration;
        StopNoticeMotion();
        // A notice already up changes its text where it is; one on its way down comes back up from where it got to.
        var resting = _notice.IsOpen ? _noticeSlide.Y : (double?)null;
        _notice.Severity = severity; _notice.Title = title; _notice.Message = message; _notice.ActionButton = action;
        LastNotice = (severity, title, message, brief);
        _notice.IsOpen = true;
        _ = SlideNoticeAsync(generation, brief, resting);
    }

    /// <summary>
    /// The slide, in the notice's own height so its top edge rises from the status bar. Held notices slide up and stay; a
    /// brief one slides down again after its hold. Any newer notice, or a close, ends this one's motion where it is.
    /// </summary>
    private async Task SlideNoticeAsync(int generation, bool brief, double? resting)
    {
        _notice.UpdateLayout();
        var height = _notice.ActualHeight + _notice.Margin.Bottom;
        var from = resting ?? height;
        if (from > 0) await RunNoticeMotionAsync(generation, from, to: 0, NoticeInDuration, EasingMode.EaseOut);
        else _noticeSlide.Y = 0;
        if (generation != _noticeGeneration || !brief) return;
        if (!await HoldNoticeAsync(generation)) return;
        if (height > 0) await RunNoticeMotionAsync(generation, from: 0, to: height, NoticeOutDuration, EasingMode.EaseIn);
        if (generation != _noticeGeneration) return;
        _notice.IsOpen = false;
    }

    private Task<bool> HoldNoticeAsync(int generation)
    {
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = _noticeTimer = DispatcherQueue.CreateTimer();
        timer.Interval = BriefNoticeHold;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { if (ReferenceEquals(_noticeTimer, timer)) _noticeTimer = null; held.TrySetResult(generation == _noticeGeneration); };
        timer.Start();
        return held.Task;
    }

    private async Task RunNoticeMotionAsync(int generation, double from, double to, TimeSpan duration, EasingMode easing)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = new Duration(duration), EasingFunction = new ExponentialEase { EasingMode = easing, Exponent = easing == EasingMode.EaseOut ? 5 : 4 } };
        Storyboard.SetTarget(animation, _noticeSlide);
        Storyboard.SetTargetProperty(animation, "Y");
        var motion = new Storyboard();
        motion.Children.Add(animation);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        motion.Completed += (_, _) => completed.TrySetResult();
        _noticeMotion = motion;
        _noticeSlide.Y = from;
        motion.Begin();
        // The compositor does not tick while the window is hidden; a slide that never completes must not hold the notice half-way.
        await Task.WhenAny(completed.Task, Task.Delay(duration + TimeSpan.FromSeconds(1)));
        if (!ReferenceEquals(_noticeMotion, motion) || generation != _noticeGeneration) return;
        _noticeMotion = null;
        motion.Stop();
        _noticeSlide.Y = to;
    }

    /// <summary>Ends a slide or a hold under way, leaving the notice where the slide got to.</summary>
    private void StopNoticeMotion()
    {
        if (_noticeMotion is { } motion)
        {
            _noticeMotion = null;
            // Stop hands the property back to its local value, so what the animation reached is kept as that value first.
            var reached = _noticeSlide.Y;
            motion.Stop();
            _noticeSlide.Y = reached;
        }
        if (_noticeTimer is { } timer) { _noticeTimer = null; timer.Stop(); }
    }

    /// <summary>The notice closed, by its X or in code: nothing pending may close or move what is shown next.</summary>
    private void RestNotice()
    {
        _noticeGeneration++;
        StopNoticeMotion();
        _noticeSlide.Y = 0;
    }

    /// <summary>
    /// A turn ends by time as well as by event (output stops; nothing arrives to say so), so every tab's presence is re-read
    /// twice a second while a pane is running: only a running pane's presence changes with time (<see cref="PresenceTracker.QuietAfter"/>);
    /// an ended pane's changes by event alone, which reaches <see cref="RefreshPresence"/> on its own. Transitions are found on
    /// the next read, which bounds how late a tab turns idle or asks for attention. Called as a pane starts; the read after the
    /// last pane ended lands its final state, and the timer rests until the next start.
    /// </summary>
    private void WatchPresence()
    {
        if (_closing) return;
        if (_presenceTimer is null)
        {
            _presenceTimer = DispatcherQueue.CreateTimer();
            _presenceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _presenceTimer.Tick += (_, _) =>
            {
                foreach (var host in _open.Values.ToArray()) RefreshPresence(host);
                if (!AllPanes.Any(pane => pane.IsRunning)) _presenceTimer.Stop();
            };
        }
        if (!_presenceTimer.IsRunning) _presenceTimer.Start();
    }

    private void UpdateStatus(string? detail = null)
    {
        var running = AllPanes.Count(pane => pane.IsRunning);
        var working = AllPanes.Count(pane => pane.IsRunning && pane.PresenceState == PaneState.Working);
        // Panes whose agent says it waits for the user, or whose turn ended unseen without saying how (docs/decisions/program-status.md).
        var waiting = AllPanes.Count(pane => pane.IsRunning && pane.PresenceState is PaneState.Blocked or PaneState.Attention);
        // The tabs are the strip's, the current workspace's; the running counts are the window's, since every pane costs the same wherever it sits.
        var tabs = _tabs.TabItems.Count;
        var where = _workspaces.Count > 1 && _workspace is not null ? WorkspaceName(_workspace) + " · " : "";
        _statusDot.Fill = running > 0 ? _theme.SuccessDot : _theme.Neutral;
        _statusText.Text = where + (running > 0
            ? $"{running} running{(working > 0 ? $" · {working} working" : "")}{(waiting > 0 ? $" · {waiting} {(waiting == 1 ? "needs" : "need")} you" : "")} · {tabs} {(tabs == 1 ? "tab" : "tabs")}"
            : $"{tabs} {(tabs == 1 ? "tab" : "tabs")} · nothing running");
        if (detail is not null) _statusDetail.Text = detail;
        _welcome.Visibility = tabs == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshBlink();
        FollowLimits();
        // The rail's Resume all: in the rail accent, standing out, while something waits to be resumed; otherwise, and while a
        // run is under way, disabled but drawn in the rail's text colour like the glyphs round it, not dimmed. The icon keeps
        // its own brush, so the button's template does not grey it. Both brushes are live, so a theme change recolours it.
        // Disabled, its tooltip says why, keeping the key so the button is still named.
        var resumable = !ResumingAll && AllPanes.Any(pane => !pane.IsRunning && AgentOn(pane.Descriptor.AgentId));
        SetRailButton("resume", resumable, resumable ? "Resume all (F5)" : ResumingAll ? "Resuming all (F5)" : "Nothing to resume (F5)");
        _resumeGlyph.Fill = resumable ? _theme.RailAccent : _theme.RailText;
    }
}
