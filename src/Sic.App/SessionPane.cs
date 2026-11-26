using Sic.App.Controls;
using Sic.Core.Agents;
using Sic.Core.Appearance;
using Sic.Core.Limits;
using Sic.Core.Sessions;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Sic.App;

internal enum PaneStatus { Idle, Starting, Running, Exited, Failed }

/// <summary>A start refused because the pane already holds a terminal, running or still starting. The message is the short form; the rest is for <see cref="StartConflict"/>.</summary>
internal sealed class PaneStartedException(StartRequest holder, bool running, int? processId, DateTimeOffset? processStartedAt)
    : InvalidOperationException(running ? "This pane is already running." : "This pane is still starting.")
{
    internal StartRequest Holder { get; } = holder;
    internal bool Running { get; } = running;
    internal int? ProcessId { get; } = processId;
    internal DateTimeOffset? ProcessStartedAt { get; } = processStartedAt;
}

/// <summary>
/// One terminal slot inside a tab: a compact header (agent, the pane's name, its folder, status, and the find, split and
/// close buttons), an optional find bar, and either a live terminal or a saved-conversation placeholder. The name is the
/// pane's own (<see cref="TabLabels.PaneTitle"/>) and is renamed in place, like a tab's.
/// </summary>
internal sealed class SessionPane : Grid, IAsyncDisposable
{
    private readonly string _browserDirectory;
    private readonly AppPreferences _preferences;
    private readonly Theme _theme;
    /// <summary>The tab's paint, whose header brushes this pane's header uses; its dot brushes are vivid against the header's surface. Swapped when the pane moves to another tab (<see cref="Rechrome"/>).</summary>
    private TabPaint _chrome;
    private readonly Func<SessionPane, Task> _resume;
    private readonly Func<SessionPane, Task> _link;
    private readonly Func<SessionPane, string, Task> _rename;
    /// <summary>The emoji of the pinned folder a working folder is in, or null (MainWindow.FolderMarkFor).</summary>
    private readonly Func<string, string?> _folderMark;
    /// <summary>Asks whether to send text of several lines to this PowerShell pane, as it will be sent (MainWindow.ConfirmPasteAsync).</summary>
    private readonly Func<string, Task<bool>> _confirmPaste;
    /// <summary>Whether the pane has a saved conversation to pin to the top of Sessions (MainWindow.PinnableKey).</summary>
    private readonly Func<SessionPane, bool> _canPin;
    // The terminal or the placeholder, and over it the accent border that marks the pane with the keyboard in a split tab
    // (SetActive). The border is drawn on top, in the terminal page's margin around the cells, so its width (Settings → Active
    // pane border) resizes nothing, and an inactive pane shows no frame of the pane's own color round a terminal of another.
    private readonly Grid _frame = new();
    private readonly ContentControl _viewport = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Border _ringLine = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly Border _tileHost = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name;
    /// <summary>The folder emoji before the name (docs/decisions/folder-emoji.md); collapsed while the folder has none.</summary>
    private readonly TextBlock _mark;
    private readonly TextBlock _path;
    // Off-tree twin of the header path, used only to measure candidate strings in the same font.
    private readonly TextBlock _pathMeasure = new() { FontFamily = Theme.Mono, FontSize = 11 };
    // The name and the folder share the header's stretching column: the name takes what it needs, up to a limit, and the
    // folder the rest. The TextBlocks' own widths follow their text; the column's is the room available.
    private readonly Grid _titleArea = new() { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly ColumnDefinition _pathColumn = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly TextBlock _state;
    private readonly Ellipse _dot;
    private readonly Button _pinButton;
    private bool _pinShown;
    private readonly StackPanel _context;
    private readonly Grid _contextMeter;
    private readonly Border _contextTrack, _contextFill;
    private readonly TextBlock _contextModel, _contextText;
    private readonly Button _linkButton;
    private readonly Button _splitButton;
    private readonly MenuFlyoutItem _splitRight, _splitDown;
    private readonly Button _folderButton;
    private readonly StackPanel _actions;
    private readonly Grid _header;
    private readonly FindBar _find;
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _ring = new(Microsoft.UI.Colors.Transparent);
    // The saved conversation the placeholder last showed, so a failed start can show it again.
    private Conversation? _previewed;

    internal SavedPane Descriptor { get; private set; }
    internal TerminalView? Terminal { get; private set; }
    internal PaneStatus Status { get; private set; } = PaneStatus.Idle;
    internal int? ExitCode { get; private set; }
    internal bool IsRunning => Terminal?.IsRunning == true;
    /// <summary>What asked for the terminal the pane holds or is starting, so a request that finds it taken can say which came first.</summary>
    internal StartRequest? CurrentStart { get; private set; }
    private bool _starting;
    private readonly SingleFlight<bool> _exit = new();
    private readonly SingleFlight<bool> _resuming = new();
    /// <summary>True from the moment a resume is asked for until it has started the terminal or failed, checks of the saved history included.</summary>
    internal bool Resuming => _resuming.Running;
    internal FindBar Find => _find;
    /// <summary>The header's fill: the owning tab's selected fill, so a colored active tab washes its pane headers too.</summary>
    internal Microsoft.UI.Xaml.Media.Brush HeaderFill => _header.Background;
    /// <summary>The header itself, for verification.</summary>
    internal Grid Header => _header;
    /// <summary>The header's split button, which the welcome tour points at.</summary>
    internal Button SplitButton => _splitButton;
    /// <summary>False once the tab holds as many panes as it can; the split button and menu items are disabled.</summary>
    internal bool CanSplit
    {
        get => _splitButton.IsEnabled;
        // The glyph carries its own ink, which the button's disabled state does not dim.
        set { _splitButton.IsEnabled = value; _splitButton.Opacity = value ? 1 : 0.4; }
    }
    /// <summary>Whether the pane, at its laid-out size, has the room to be split this way: two minimum panes and the divider. Unknown sizes do not refuse.</summary>
    internal bool FitsSplit(PaneSplit direction) => PaneLayout.CanSplit(direction, ActualWidth, ActualHeight, PaneSplitter.GrabSize);
    /// <summary>Whether the pane's tab keeps its panes even, as the window last said: what the Keep panes even items show ticked.</summary>
    internal bool KeepEven { get; set; }
    /// <summary>The sides the pane's terminal takes Alt+Arrow toward (<see cref="TerminalView.Neighbors"/>), kept for a terminal started later too.</summary>
    internal PaneNeighbors Neighbors
    {
        get => _neighbors;
        set { _neighbors = value; if (Terminal is { } terminal) terminal.Neighbors = value; }
    }
    private PaneNeighbors _neighbors;
    /// <summary>
    /// Whether the header runs along the pane's bottom, under the terminal, as every bar does with the bars at the bottom
    /// (Settings → Tabs), so the selected tab under a bottom pane meets its header as it does at the top. The find bar stays
    /// beside the header, between it and the terminal.
    /// </summary>
    internal bool HeaderAtBottom
    {
        get => _headerAtBottom;
        set { if (_headerAtBottom != value) { _headerAtBottom = value; PlaceHeader(); } }
    }
    private bool _headerAtBottom;
    /// <summary>When the pane last became its tab's focused pane, as a running count (0 for never): Alt+Arrow prefers the pane focused last, and a tab shown again gives it the focus back.</summary>
    internal long LastActive { get; set; }
    /// <summary>Whether the agent is working, idle, or waiting for the user: fed by the terminal, re-read by the window twice a second.</summary>
    internal PresenceTracker Presence { get; private set; }
    /// <summary>The state <see cref="RefreshPresence"/> last read.</summary>
    internal PaneState PresenceState { get; private set; }
    /// <summary>The header's status word, for verification.</summary>
    internal string StatusText => _state.Text;
    /// <summary>The name the header shows, for verification.</summary>
    internal string NameText => _name.Text;
    /// <summary>The folder emoji as the header shows it; empty while the folder has none.</summary>
    internal string FolderMarkText => _mark.Text;

    /// <summary>Re-reads the emoji of the pinned folder this pane works in, before its name: on creation, a link, a pin or a mark change.</summary>
    internal void RefreshFolderMark()
    {
        var mark = _folderMark(Descriptor.WorkingDirectory) ?? "";
        if (_mark.Text != mark) _mark.Text = mark;
        _mark.Visibility = mark.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(_mark, mark.Length == 0 ? null : "The emoji of this pane's pinned folder");
    }
    /// <summary>The in-place rename box while a rename is open.</summary>
    internal TextBox? Editor { get; private set; }
    /// <summary>The text this pane last put on the clipboard, for verification.</summary>
    internal string? LastCopied { get; private set; }
    /// <summary>The text the program in this pane last put on the clipboard (OSC 52), for the menu's Copy after a selection it made itself.</summary>
    private string? _programCopy;
    /// <summary>
    /// The program put this text on the clipboard, this long after the user's last key, click or drag in the pane (null when
    /// there was none); the window decides what to say (<see cref="ClipboardText.NoticeForProgramCopy"/>), since anything a pane prints can copy.
    /// </summary>
    internal event Action<SessionPane, string, TimeSpan?>? ProgramCopied;
    /// <summary>The program asked to put this many characters on the clipboard, too many for SIC to take; nothing was copied.</summary>
    internal event Action<SessionPane, int>? ProgramCopyRefused;
    internal event Action<SessionPane>? Selected;
    internal event Action? StatusChanged;
    internal event Action<SessionPane>? PresenceChanged;
    /// <summary>A split of this pane, into columns (the new pane to the right) or rows (below).</summary>
    internal event Action<SessionPane, PaneSplit>? SplitRequested;
    /// <summary>Keep panes even ticked or unticked on the pane's menus, for its tab (docs/decisions/keep-panes-even.md).</summary>
    internal event Action<SessionPane, bool>? KeepEvenRequested;
    /// <summary>A new session of this pane's agent in this pane's folder, in a tab of its own.</summary>
    internal event Action<SessionPane>? DuplicateRequested;
    /// <summary>This pane out of its split tab into a tab of its own.</summary>
    internal event Action<SessionPane>? MoveOutRequested;
    /// <summary>The pane's menu is being built: the window adds what only it knows, Move to workspace (MainWindow.Workspaces.cs).</summary>
    internal event Action<SessionPane, MenuFlyout>? ExtendingMenu;
    /// <summary>This pane's conversation pinned to the top of Sessions, or unpinned while it is there (<see cref="IsPinned"/>); the pane stays.</summary>
    internal event Action<SessionPane>? PinRequested;
    /// <summary>This pane's conversation pinned to the top of Sessions and this pane alone closed.</summary>
    internal event Action<SessionPane>? PinAndCloseRequested;
    /// <summary>The key the pane's conversation is pinned and named under, or null for a shell or a pane not linked to one.</summary>
    internal string? ConversationKey => Descriptor.AgentId != "shell" && Descriptor.SessionId is { } id ? TabLabels.Key(Descriptor.AgentId, id) : null;
    /// <summary>Whether the pane's conversation is at the top of Sessions.</summary>
    internal bool IsPinned => ConversationKey is { } key && _preferences.PinnedConversations.Contains(key);
    /// <summary>Whether the pane has a conversation to pin, as the window judges it (MainWindow.PinnableKey); the pin items are greyed otherwise.</summary>
    internal bool CanPin => _canPin(this);
    /// <summary>True once the user has typed or pasted into a terminal of this pane, in this run; a new session nothing was typed into has no transcript to pin.</summary>
    internal bool TypedInto { get; private set; }
    /// <summary>
    /// The header's pin button (MainWindow.RefreshPinMarks): a filled pin while the pane's conversation is at the top of Sessions,
    /// or its tab is pinned whole; a hollow one otherwise, dimmed while the pane has nothing to pin yet (<paramref name="pinnable"/>,
    /// as the window judges it at each draw, so a pane that gains its identity or its first keystroke shows it without waiting).
    /// A click always asks (<see cref="PinRequested"/>): the window pins, unpins, or says why it cannot. The button is always
    /// there, so the header's buttons keep their places.
    /// </summary>
    internal void ShowPinned(bool pinned, bool pinnable)
    {
        _pinShown = pinned;
        RenderPin(pinnable);
    }

    /// <summary>The pin as the header draws it: filled or hollow, and in which header ink; the glyph is made again only when one of them changes.</summary>
    private (bool Filled, Microsoft.UI.Xaml.Media.Brush Ink)? _pinDrawn;

    private void RenderPin() => RenderPin(_pinShown || CanPin);

    private void RenderPin(bool pinnable)
    {
        pinnable |= _pinShown;
        // Drawn on every status change of the tab: the glyph (a path parsed from its data, or an icon) is kept between draws.
        if (_pinDrawn != (_pinShown, _chrome.HeaderInk))
        {
            _pinButton.Content = _pinShown ? PinMark.Create(_chrome.HeaderInk, 11) : Theme.Icon(Theme.PinGlyph, 11, _chrome.HeaderInk);
            _pinDrawn = (_pinShown, _chrome.HeaderInk);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_pinButton, _pinShown ? "Unpin pane" : "Pin pane");
        }
        var opacity = pinnable ? 1 : 0.45;
        if (_pinButton.Opacity != opacity) _pinButton.Opacity = opacity;
        var words = _pinShown ? "Unpin" : pinnable ? "Pin to the top of Sessions" : "Nothing to pin yet";
        if (ToolTipService.GetToolTip(_pinButton) is not string current || current != words) ToolTipService.SetToolTip(_pinButton, words);
    }
    /// <summary>Whether the header shows the pinned mark, for verification.</summary>
    internal bool PinShown => _pinShown;
    /// <summary>The header's pin button, for verification.</summary>
    internal Button PinButton => _pinButton;
    /// <summary>A press on the header has moved far enough to be a drag of the pane: the window takes the pointer and shows where it would land.</summary>
    internal event Action<SessionPane, PointerRoutedEventArgs>? MoveRequested;
    /// <summary>How far a press on the header moves before it is a drag of the pane rather than a click.</summary>
    internal const double MoveThreshold = 4;
    private Windows.Foundation.Point? _pressedAt;
    private uint _pressedPointer;
    /// <summary>False in a tab of one pane, which already is a tab of its own; the Move pane to new tab item is greyed.</summary>
    internal bool CanMoveOut { get; set; }

    /// <summary>Whether the pane is shown as a card over its tab's other panes (MainWindow.Zoom.cs); the pane's menu offers Zoom out then.</summary>
    internal bool IsZoomed { get; set; }

    /// <summary>The pane's menu asked to zoom the pane, or to put it back while it is zoomed.</summary>
    internal event Action<SessionPane>? ZoomRequested;
    /// <summary>The placeholder's Turn on, for an agent switched off in Settings: the agent's ID.</summary>
    internal event Action<string>? TurnOnAgentRequested;

    /// <summary>The placeholder's Start new session here (<see cref="FreshStart.Offered"/>).</summary>
    internal event Action<SessionPane>? StartNewHereRequested;
    /// <summary>
    /// Why the last resume found no saved history for the pane's conversation, shown on the placeholder beside Start new
    /// session here; null when it has not failed so. A new identity (a link, a new session) clears it.
    /// </summary>
    internal string? HistoryMissing { get; set; }

    /// <summary>Whether this pane's agent is switched off in Settings → Agents; PowerShell never is.</summary>
    private bool AgentOff => Descriptor.AgentId != "shell" && !_preferences.Agents.Any(agent => agent.Id == Descriptor.AgentId && agent.IsOn);

    /// <summary>
    /// Whether the placeholder offers Link saved conversation…: an agent pane with no session ID that is not a new session
    /// nothing has been saved in yet (that one starts again on Start session), or a conversation whose folder nothing knows,
    /// which the Link dialog's folder box alone can set, since SicTerm never guesses one. Every agent placeholder had the
    /// button until October 16, 2026, and on a new session closed before its first message it was the one button that led nowhere.
    /// </summary>
    private bool LinkOffered => Descriptor.AgentId != "shell" && (Descriptor.SessionId is null && !Descriptor.Unsaved || Descriptor.WorkingDirectory.Length == 0);

    /// <summary>
    /// The header's Link, for a pane with no session ID whose placeholder is not showing or would not offer Link: a Codex session
    /// that could not be bound (two candidates, or the terminal has exited) has no other route to its identity. A new session
    /// nothing has been saved in yet shows it only once its terminal is in the pane.
    /// </summary>
    private void RefreshLinkButton() =>
        _linkButton.Visibility = Descriptor.AgentId != "shell" && Descriptor.SessionId is null && (Terminal is not null || !Descriptor.Unsaved) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Draws the placeholder again, as when an agent's switch changed; a running pane is left as it is.</summary>
    internal void RefreshPreview() { if (Terminal is null) ShowPreview(_previewed, PreviewWarning); }
    internal event Action<SessionPane>? CloseRequested;
    internal event Action<string>? Shortcut;

    internal SessionPane(SavedPane descriptor, string browserDirectory, AppPreferences preferences, Theme theme, TabPaint chrome,
        Func<SessionPane, Task> resume, Func<SessionPane, Task> link, Func<SessionPane, string, Task> rename, Func<string, string?> folderMark,
        Func<string, Task<bool>> confirmPaste, Func<SessionPane, bool> canPin)
    {
        Descriptor = descriptor; _browserDirectory = browserDirectory; _preferences = preferences; _chrome = chrome;
        _theme = theme; _resume = resume; _link = link; _rename = rename; _folderMark = folderMark; _confirmPaste = confirmPaste; _canPin = canPin;
        Presence = new PresenceTracker(descriptor.AgentId, selected: false);
        Background = _theme.Terminal;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // The active tab is filled with this same brush, so the tab flows into its pane header without a seam.
        var header = _header = new Grid { Background = chrome.Selected, BorderBrush = _theme.Stroke, BorderThickness = new Thickness(0, 0, 0, 1), Height = 24, Padding = new Thickness(8, 0, 2, 0), ColumnSpacing = 6 };
        // The tile, the folder emoji, the name and folder (stretching), the state, Link, and the buttons.
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tileHost.Child = _theme.AgentTile(descriptor.AgentId, 14);
        header.Children.Add(_tileHost);
        _mark = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        SetColumn(_mark, 1); header.Children.Add(_mark);
        // A transparent fill makes the whole name-and-folder area, not just the glyphs, a double-click target for the rename.
        _titleArea.Background = _theme.Transparent;
        _titleArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleArea.ColumnDefinitions.Add(_pathColumn);
        _name = _theme.BodyText(Label, 11, 600);
        _name.Foreground = chrome.SelectedInk; _name.MaxWidth = 180;
        _titleArea.Children.Add(_name);
        _path = _theme.MonoText(descriptor.WorkingDirectory);
        _path.FontSize = 11; _path.Foreground = chrome.HeaderInk;
        // The folder's room follows the header's width and what the name takes of it; the name's width settles a layout pass after its limit is set.
        header.SizeChanged += (_, _) => FitPath();
        // A press on the header that moves MoveThreshold px begins a move of the pane (MoveRequested): the window takes the pointer
        // from there. No capture here, which would keep the double-tap from the name-and-folder area; the header is 24 px tall and
        // the threshold small, so the move begins before the pointer can leave it. The buttons handle their own presses.
        header.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(header);
            if (!point.Properties.IsLeftButtonPressed || Editor is not null) return;
            _pressedAt = point.Position; _pressedPointer = args.Pointer.PointerId;
        };
        header.PointerMoved += (_, args) =>
        {
            if (_pressedAt is not { } from || args.Pointer.PointerId != _pressedPointer || !args.GetCurrentPoint(header).Properties.IsLeftButtonPressed) { _pressedAt = null; return; }
            var at = args.GetCurrentPoint(header).Position;
            if (Math.Abs(at.X - from.X) < MoveThreshold && Math.Abs(at.Y - from.Y) < MoveThreshold) return;
            _pressedAt = null;
            MoveRequested?.Invoke(this, args);
        };
        header.PointerReleased += (_, _) => _pressedAt = null;
        header.PointerCaptureLost += (_, _) => _pressedAt = null;
        header.PointerCanceled += (_, _) => _pressedAt = null;
        // A right-click on the header opens the pane's menu, as a right-click on a tab opens the tab's: the one route to the pane
        // items for a pane that is not running, since the placeholder has no terminal to right-click in.
        header.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            ShowPaneMenu(header, args.TryGetPosition(header, out var point) ? point : null);
        };
        _titleArea.SizeChanged += (_, _) => FitPath();
        _name.SizeChanged += (_, _) => FitPath();
        ToolTipService.SetToolTip(_path, descriptor.WorkingDirectory);
        SetColumn(_path, 1); _titleArea.Children.Add(_path);
        _titleArea.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler((_, args) =>
        {
            if (Editor is not null) return;
            args.Handled = true;
            BeginRename();
        }), handledEventsToo: true);
        SetColumn(_titleArea, 2); header.Children.Add(_titleArea);
        RefreshName();
        _dot = Theme.Dot(_theme.Neutral, 5);
        _state = _theme.Tertiary("Not started");
        _state.FontSize = 11; _state.Foreground = chrome.HeaderInkMuted;
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        // The context window of a Claude Code session, before the status dot (docs/decisions/context-window.md): a meter like the
        // status bar's and the share used, shown while the pane runs and its session has reported one (ShowContext).
        _context = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0), Visibility = Visibility.Collapsed };
        // The model's name first, in the header's secondary ink, then the meter and the share; an agent that reports no usage shows the name alone.
        _contextModel = _theme.Tertiary("");
        _contextModel.FontSize = 11; _contextModel.Foreground = chrome.HeaderInk; _contextModel.MaxWidth = 140;
        _contextMeter = new Grid { Width = ContextMeterWidth, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        _contextTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = chrome.HeaderInkMuted, Opacity = 0.35, VerticalAlignment = VerticalAlignment.Center };
        _contextFill = new Border { Height = 4, Width = 2, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        _contextMeter.Children.Add(_contextTrack); _contextMeter.Children.Add(_contextFill);
        _contextText = _theme.Tertiary("");
        _contextText.FontSize = 11; _contextText.Foreground = chrome.HeaderInkMuted;
        _context.Children.Add(_contextModel); _context.Children.Add(_contextMeter); _context.Children.Add(_contextText);
        status.Children.Add(_context); status.Children.Add(_dot); status.Children.Add(_state);
        SetColumn(status, 3); header.Children.Add(status);
        _linkButton = Theme.TextButton("Link", "Associate this pane with an exact saved conversation");
        _linkButton.Padding = new Thickness(6, 0, 6, 1); _linkButton.MinHeight = 20; _linkButton.FontSize = 11;
        RefreshLinkButton();
        _linkButton.Click += async (_, _) => await _link(this);
        SetColumn(_linkButton, 4); header.Children.Add(_linkButton);
        var actions = _actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        // The pin first among the buttons, always there: filled while the conversation (or the tab, whole) is pinned, hollow
        // otherwise, dimmed with nothing to pin (ShowPinned). Until October 16, 2026 a filled mark stood before the status
        // while pinned, and the tab carried one after its title.
        _pinButton = _theme.MarkButton(new Border(), "Pin to the top of Sessions", 22);
        _pinButton.Click += (_, _) => PinRequested?.Invoke(this);
        actions.Children.Add(_pinButton);
        RenderPin();
        _folderButton = _theme.IconButton("", "Open the working folder in File Explorer (Ctrl+Shift+E)", 22, 11, chrome.HeaderInk);
        _folderButton.Click += (_, _) => OpenFolder();
        UpdateFolderButton();
        var find = _theme.IconButton("", "Find in terminal (Ctrl+Shift+F)", 22, 11, chrome.HeaderInk);
        find.Click += (_, _) => OpenFind();
        _splitButton = _theme.IconButton("", "Split this pane: a new session to its right or below it", 22, 11, chrome.HeaderInk);
        var splits = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        _splitRight = SplitItem(PaneSplit.Columns);
        _splitDown = SplitItem(PaneSplit.Rows);
        var keepEven = KeepEvenItem();
        splits.Items.Add(_splitRight); splits.Items.Add(_splitDown); splits.Items.Add(keepEven);
        splits.Opening += (_, _) => { RefreshSplitItems(_splitRight, _splitDown); keepEven.IsChecked = KeepEven; };
        _splitButton.Flyout = splits;
        var close = _theme.IconButton("", "Close pane", 22, 10, chrome.HeaderInk);
        close.Click += (_, _) => CloseRequested?.Invoke(this);
        actions.Children.Add(_folderButton); actions.Children.Add(find); actions.Children.Add(_splitButton); actions.Children.Add(close);
        SetColumn(actions, 5); header.Children.Add(actions);
        Children.Add(header);

        _find = new FindBar(_theme);
        // Only a terminal whose page has loaded is searched. These handlers are async void: an exception leaving one ends SIC
        // and every agent with it, so a failure is reported in the header instead.
        _find.FindRequested += async (query, backward, incremental) =>
        {
            if (Terminal is not { Ready.IsCompletedSuccessfully: true } terminal) return;
            try { await terminal.FindAsync(query, backward, incremental); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { SetStatus(Status, "Error: " + exception.Message); }
        };
        _find.Closed += async () =>
        {
            if (Terminal is not { Ready.IsCompletedSuccessfully: true } terminal) return;
            try { await terminal.ClearFindAsync(); await terminal.FocusInputAsync(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { SetStatus(Status, "Error: " + exception.Message); }
        };
        SetRow(_find, 1); Children.Add(_find);

        _ringLine.BorderBrush = _ring;
        _frame.Children.Add(_viewport);
        _frame.Children.Add(_ringLine);
        SetRow(_frame, 2); Children.Add(_frame);
        // GotFocus is raised asynchronously, after the focus may have moved on: Alt+Arrow pressed twice in quick succession moves
        // it twice before the first pane hears of it, and a pane selected then would take the focused pane back a step. A pane
        // is selected only while it still holds the focus.
        GotFocus += (_, _) => { if (HoldsFocus()) Selected?.Invoke(this); };
        PointerPressed += (_, _) => Selected?.Invoke(this);
        Loaded += FocusWhenLoaded;
        RefreshFolderMark();
        // The placeholder page is the window's to show (ShowPreview), with the saved conversation when the pane has one: built
        // here as well, every restored pane's page was made twice.
    }

    /// <summary>The name the pane goes by: its conversation's local name, its own, or its conversation's title (<see cref="TabLabels.PaneTitle"/>).</summary>
    internal string DisplayName => TabLabels.PaneTitle(Descriptor, _preferences.ConversationNames);

    /// <summary>The name as the header shows it: without the harness name, which the agent tile beside it already says.</summary>
    internal string Label => TabLabels.WithoutAgentName(DisplayName);

    /// <summary>Re-reads the name after a rename, a link, or a change to the conversation's local name.</summary>
    internal void RefreshName()
    {
        var label = Label;
        if (_name.Text != label) _name.Text = label;
        // The folder rides along, since a narrow pane shows the name alone.
        ToolTipService.SetToolTip(_name, DisplayName + "\n" + Descriptor.WorkingDirectory + "\nDouble-click to rename this pane (F2)");
        AutomationProperties.SetName(_name, "Pane name: " + DisplayName);
    }

    /// <summary>A split item for the split button's flyout and for the pane's right-click menu, with the direction's shortcut.</summary>
    private MenuFlyoutItem SplitItem(PaneSplit direction)
    {
        var columns = direction == PaneSplit.Columns;
        var item = Theme.SplitMenuItem(direction);
        ToolTipService.SetToolTip(item, columns ? "A new session to the right of this pane, in the same tab" : "A new session below this pane, in the same tab");
        item.Click += (_, _) => SplitRequested?.Invoke(this, direction);
        return item;
    }

    /// <summary>The Keep panes even toggle for the split button's flyout and the pane's right-click menu: the tab's choice, which the window keeps (docs/decisions/keep-panes-even.md).</summary>
    private ToggleMenuFlyoutItem KeepEvenItem()
    {
        var item = new ToggleMenuFlyoutItem { Text = "Keep panes even", IsChecked = KeepEven };
        ToolTipService.SetToolTip(item, Theme.KeepEvenTip);
        item.Click += (_, _) => KeepEvenRequested?.Invoke(this, item.IsChecked);
        return item;
    }

    /// <summary>A direction the pane has no room for is offered greyed, so the refusal is seen before the click.</summary>
    private void RefreshSplitItems(MenuFlyoutItem right, MenuFlyoutItem down)
    {
        right.IsEnabled = CanSplit && FitsSplit(PaneSplit.Columns);
        down.IsEnabled = CanSplit && FitsSplit(PaneSplit.Rows);
    }

    /// <summary>Turns the header's name into a text box in place. Enter or clicking away saves, Esc cancels.</summary>
    internal void BeginRename()
    {
        if (Editor is { } open) { open.Focus(FocusState.Programmatic); return; }
        // Exactly the header's height less its rule, so renaming does not move the terminal.
        var editor = InlineRename.Editor(_theme, Label, 22, 11, 320, "Pane name", EndRenameAsync);
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        Editor = editor;
        _name.Visibility = _path.Visibility = Visibility.Collapsed;
        SetColumnSpan(editor, 2);
        _titleArea.Children.Add(editor);
    }

    /// <summary>Closes the in-place editor. Only a keyboard finish hands focus back to the terminal; clicking away leaves focus where the click put it.</summary>
    internal async Task EndRenameAsync(bool commit, bool refocus)
    {
        if (Editor is not { } editor) return;
        Editor = null;
        var name = editor.Text.Trim();
        _titleArea.Children.Remove(editor);
        _name.Visibility = _path.Visibility = Visibility.Visible;
        // The window reports what went wrong; a failure here must not escape an event handler.
        if (commit && name.Length > 0 && name != Label) await _rename(this, name);
        if (refocus && IsRunning && Terminal is { } terminal) _ = terminal.FocusInputAsync();
    }

    /// <summary>
    /// Lays the header, the find bar and the terminal from the top down, or the other way up (<see cref="HeaderAtBottom"/>): the
    /// header's rule on its side towards the terminal, and the split menu opening away from the window's edge.
    /// </summary>
    private void PlaceHeader()
    {
        var bottom = _headerAtBottom;
        RowDefinitions[0].Height = bottom ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RowDefinitions[2].Height = bottom ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        SetRow(_header, bottom ? 2 : 0);
        SetRow(_frame, bottom ? 0 : 2);
        _header.BorderThickness = bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1);
        if (_splitButton.Flyout is MenuFlyout splits) splits.Placement = bottom ? FlyoutPlacementMode.Top : FlyoutPlacementMode.Bottom;
    }

    /// <summary>Draws the border, <see cref="RingWidth"/> wide in <see cref="RingColor"/> or the theme's accent, that marks the pane receiving keyboard input.</summary>
    internal void SetActive(bool active) => _ring.Color = active ? RingColor ?? _theme.Accent.Color : Microsoft.UI.Colors.Transparent;

    /// <summary>The border's color when one is chosen (Settings → Session Indicator Color); null for the theme's accent. Shown by the next <see cref="SetActive"/>.</summary>
    internal Windows.UI.Color? RingColor { get; set; }

    /// <summary>The accent border's width in DIPs (Settings → Session Indicator Width, <see cref="PaneBorder"/>).</summary>
    internal double RingWidth
    {
        get => _ringLine.BorderThickness.Left;
        set => _ringLine.BorderThickness = new Thickness(value);
    }

    /// <summary>The accent border itself, for verification.</summary>
    internal Border RingLine => _ringLine;

    /// <summary>
    /// Takes another tab's paint, for a pane moved into that tab: the header's fill and every ink on it are that tab's live
    /// brushes from here on, so the header follows the tab's color and selection as the tab's other panes do.
    /// </summary>
    internal void Rechrome(TabPaint chrome)
    {
        _chrome = chrome;
        _header.Background = chrome.Selected;
        _name.Foreground = chrome.SelectedInk;
        _path.Foreground = chrome.HeaderInk;
        _state.Foreground = chrome.HeaderInkMuted;
        _contextTrack.Background = chrome.HeaderInkMuted;
        _contextText.Foreground = chrome.HeaderInkMuted;
        _contextModel.Foreground = chrome.HeaderInk;
        foreach (var button in _actions.Children.OfType<Button>()) if (button.Content is FontIcon icon) icon.Foreground = chrome.HeaderInk;
        RenderPin();
        // The dot's running and starting brushes are the tab's too, and the context meter's fill.
        SetStatus(Status, _state.Text);
        ShowContext(Context);
    }

    /// <summary>The meter's width; its fill is the share of the context window used.</summary>
    private const double ContextMeterWidth = 30;

    /// <summary>The context reading the header shows, or null while it shows none.</summary>
    internal ContextReading? Context { get; private set; }
    /// <summary>
    /// The Claude Code process the pane's session reports from (<see cref="PaneOrigin"/>), learned from its first reading and
    /// forgotten when the pane starts again; a reading of another session from this process moves the pane to it.
    /// </summary>
    internal int? AgentProcess { get; set; }
    /// <summary>Whether the context meter is in the header, for verification.</summary>
    internal bool ContextShown => _context.Visibility == Visibility.Visible;
    /// <summary>The share the header says, for verification.</summary>
    internal string ContextText => _contextText.Text;
    /// <summary>The model the header names, for verification; empty when it names none.</summary>
    internal string ContextModelText => _contextModel.Text;
    /// <summary>Whether the meter itself is in the header (a reading with a share), for verification.</summary>
    internal bool ContextMeterShown => _contextMeter.Visibility == Visibility.Visible;
    /// <summary>The meter's fill brush, for verification.</summary>
    internal Microsoft.UI.Xaml.Media.Brush? ContextFill => _contextFill.Background;
    /// <summary>The words behind the meter (its tooltip), for verification.</summary>
    internal string? ContextWords => ToolTipService.GetToolTip(_context) as string;

    /// <summary>
    /// Shows the session's model and how full its context window is (<see cref="ContextWindow"/>): the model's name, then the
    /// meter's fill at the share used, green with room to spare, amber from <see cref="ContextWindow.FillingPercent"/> and red
    /// from <see cref="ContextWindow.NearPercent"/>, the hues the header's dots use, and the share beside it, bold when near. A
    /// reading without a share (an agent that reports no usage) shows the model alone. The tooltip adds what the header does not
    /// show: the tokens and the window's size. Null, or a reading with nothing in it, takes it all away.
    /// </summary>
    internal void ShowContext(ContextReading? reading)
    {
        Context = reading;
        if (reading is not { HasContent: true }) { _context.Visibility = Visibility.Collapsed; return; }
        var model = reading.Model?.Trim() ?? "";
        _contextModel.Text = model;
        _contextModel.Visibility = model.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (reading.UsedPercent is { } used)
        {
            _contextFill.Width = Math.Max(2, ContextMeterWidth * Math.Clamp(used, 0, 100) / 100);
            _contextFill.Background = reading.State switch
            {
                ContextState.Near => _theme.Danger,
                ContextState.Filling => _chrome.HeaderWarning,
                _ => _chrome.HeaderSuccess
            };
            _contextText.Text = ContextWindow.Share(reading);
            _contextText.FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(reading.State == ContextState.Near ? 700 : 400) };
        }
        else _contextText.Text = "";
        _contextMeter.Visibility = _contextText.Visibility = reading.UsedPercent is null ? Visibility.Collapsed : Visibility.Visible;
        var words = ContextWindow.Describe(reading);
        ToolTipService.SetToolTip(_context, words);
        AutomationProperties.SetName(_context, words);
        _context.Visibility = Visibility.Visible;
    }

    /// <summary>Whether the window's keyboard focus is on this pane or inside it.</summary>
    internal bool HoldsFocus()
    {
        if (XamlRoot is null) return false;
        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, this)) return true;
        return false;
    }

    /// <summary>
    /// Gives the pane the keyboard: its terminal when it has one, running or not, else the placeholder's first control (Resume
    /// conversation or Start terminal, so Enter starts it). Keys must not stay with a pane the accent ring has left. The panes of
    /// a tab just shown rejoin the window a layout pass later, and focus given before then is lost: a tab switched to with
    /// Ctrl+1…9 left the keyboard with nothing at all. Until the pane and its terminal have loaded, the focus waits for them,
    /// unless another pane is chosen first (<see cref="CancelFocus"/>).
    /// </summary>
    internal void FocusInput()
    {
        _focusWanted = !IsLoaded || Terminal is { IsLoaded: false };
        if (_focusWanted) return;
        if (Terminal is { } terminal) _ = terminal.FocusInputAsync();
        else if (FocusManager.FindFirstFocusableElement(_viewport) is Control first) first.Focus(FocusState.Keyboard);
    }

    private bool _focusWanted;

    /// <summary>Drops a focus still waiting for the pane to load (<see cref="FocusInput"/>): another pane has been chosen.</summary>
    internal void CancelFocus() => _focusWanted = false;

    /// <summary>
    /// A focus waiting for the pane, or its terminal, to load is given once whichever loads last has; queued, so every element
    /// loading in the same pass has loaded, and checked again then, in case another pane was chosen meanwhile.
    /// </summary>
    private void FocusWhenLoaded(object sender, RoutedEventArgs args)
    {
        if (_focusWanted) DispatcherQueue.TryEnqueue(() => { if (_focusWanted) FocusInput(); });
    }

    /// <summary>The warning the placeholder shows, null when none, for verification.</summary>
    internal string? PreviewWarning { get; private set; }

    /// <summary>
    /// The placeholder page for the pane's conversation. With <paramref name="loadBody"/>, <paramref name="conversation"/> is the
    /// record without its transcript: the page goes up at once with the transcript's place marked, the text is read through the
    /// loader off the UI thread and filled in when it arrives, and kept for the page's next rebuild (<see cref="RefreshPreview"/>).
    /// </summary>
    internal void ShowPreview(Conversation? conversation, string? warning = null, Func<Task<string?>>? loadBody = null)
    {
        _previewed = conversation; PreviewWarning = warning;
        var loading = conversation is not null && loadBody is not null;
        var page = new StackPanel { Spacing = 16, Padding = new Thickness(28, 24, 28, 24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        // The page takes the pane's menu on a right-click, as the header does; the transcript's own text keeps its copy menu.
        page.ContextRequested += (_, args) =>
        {
            if (args.OriginalSource is TextBlock { IsTextSelectionEnabled: true }) return;
            args.Handled = true;
            ShowPaneMenu(page, args.TryGetPosition(page, out var point) ? point : null);
        };
        var head = new Grid { ColumnSpacing = 14 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = _theme.AgentTile(Descriptor.AgentId, 40); tile.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(tile);
        var titles = new StackPanel { Spacing = 4 };
        var title = _theme.Heading(DisplayName, 18); title.TextWrapping = TextWrapping.Wrap; title.TextTrimming = TextTrimming.None;
        titles.Children.Add(title);
        var detail = Descriptor.Unsaved ? "New " + Theme.AgentName(Descriptor.AgentId) + " session · nothing has been saved in it yet, so Start session begins it again in this folder."
            : Descriptor.SessionId is not null ? "Saved conversation · " + Theme.AgentName(Descriptor.AgentId) + " · " + Descriptor.SessionId
            : Descriptor.AgentId == "shell" ? "A PowerShell terminal starts only when you choose Start terminal."
            : "This pane is not linked to a saved conversation. Choose Link to attach the exact session.";
        var caption = _theme.Caption(detail); caption.TextWrapping = TextWrapping.Wrap; caption.TextTrimming = TextTrimming.None;
        titles.Children.Add(caption);
        SetColumn(titles, 1); head.Children.Add(titles);
        page.Children.Add(head);

        var folder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        folder.Children.Add(Theme.Icon("", 14, _theme.TextTertiary));
        var folderText = _theme.MonoText(Descriptor.WorkingDirectory.Length > 0 ? Descriptor.WorkingDirectory : "Working folder unknown");
        folderText.TextWrapping = TextWrapping.Wrap; folderText.TextTrimming = TextTrimming.None; folderText.Foreground = _theme.Text;
        folder.Children.Add(folderText);
        page.Children.Add(folder);

        if (warning is not null) page.Children.Add(Banner(_theme.Danger, "\uE7BA", warning));
        if (HistoryMissing is { } missing && !Descriptor.Unsaved)
            page.Children.Add(Banner(_theme.Danger, "\uE7BA", missing.Replace(" No replacement conversation has been started.", "", StringComparison.Ordinal)
                + " Start new session here begins a new conversation in this folder under a new ID, so the missing one is left alone should its history come back.", maxWidth: 760));
        if (conversation is { TextIndexed: false })
            page.Children.Add(Banner(_theme.Warning, "\uE946", (conversation.TextNote ?? "Only the first message was imported.") + " Resume still opens the exact conversation.", maxWidth: 760));
        var agentOff = AgentOff;
        if (agentOff)
        {
            var name = Theme.AgentName(Descriptor.AgentId);
            var turnOn = Theme.TextButton("Turn on " + name);
            turnOn.Click += (_, _) => TurnOnAgentRequested?.Invoke(Descriptor.AgentId);
            page.Children.Add(Banner(_theme.Warning, "\uE7BA", name + " is off in Settings → Agents, so this conversation cannot be resumed until it is on.", trailing: turnOn));
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var starts = Descriptor.AgentId == "shell" || Descriptor.Unsaved;
        var label = Descriptor.AgentId == "shell" ? "Start terminal" : Descriptor.Unsaved ? "Start session" : "Resume conversation";
        var busy = starts ? "Starting…" : "Resuming…";
        var launch = _theme.PrimaryButton(label);
        var launchable = !agentOff && (starts || Descriptor.SessionId is not null || Descriptor.CreationName is not null);
        launch.IsEnabled = launchable;
        // The click shows at once that it was taken: nothing else changes on screen until the terminal takes the placeholder's
        // place. A placeholder rebuilt while a resume is under way (a rename, a link, a failed start) shows it busy until then.
        async Task BusyUntilAsync(Task flight)
        {
            launch.IsEnabled = false; launch.Content = busy;
            try { await flight; }
            finally { launch.IsEnabled = launchable; launch.Content = label; }
        }
        if (_resuming.Current is { } underway) _ = BusyUntilAsync(underway);
        launch.Click += async (_, _) => await BusyUntilAsync(_resume(this));
        actions.Children.Add(launch);
        if (LinkOffered)
        {
            var link = Theme.TextButton("Link saved conversation…");
            link.Click += async (_, _) => await _link(this);
            actions.Children.Add(link);
        }
        if (FreshStart.Offered(Descriptor, HistoryMissing is not null))
        {
            var fresh = Theme.TextButton("Start new session here", "A new " + Theme.AgentName(Descriptor.AgentId) + " session in " + Descriptor.WorkingDirectory + ", in this pane");
            fresh.IsEnabled = !agentOff;
            fresh.Click += (_, _) => StartNewHereRequested?.Invoke(this);
            actions.Children.Add(fresh);
        }
        page.Children.Add(actions);
        page.Children.Add(_theme.Divider());

        // A new session nothing has been saved in has no transcript and nothing to link; its caption says so.
        var body = loading ? "Loading transcript…" : conversation?.Body ?? (Descriptor.AgentId == "shell" || Descriptor.Unsaved ? "" : "The saved transcript appears here once the pane is linked to a conversation. Restoring this pane never starts an agent.");
        if (body.Length > 0)
        {
            page.Children.Add(_theme.Eyebrow(conversation is { TextIndexed: false } ? "First message" : "Transcript"));
            var transcript = new TextBlock { Text = Bounded(body), IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, Foreground = _theme.Text, FontFamily = Theme.Body, FontSize = 13, LineHeight = 20 };
            page.Children.Add(transcript);
            if (loading) _ = FillTranscriptAsync(conversation!, transcript, loadBody!);
        }
        _viewport.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>A notice on the placeholder page: an icon in the given ink, the text beside it, and a control after the text when one is given, all inside a bordered band.</summary>
    private Border Banner(Microsoft.UI.Xaml.Media.Brush edge, string glyph, string message, double? maxWidth = null, FrameworkElement? trailing = null)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Theme.Icon(glyph, 14, edge); icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        var text = _theme.BodyText(message, 13);
        text.TextWrapping = TextWrapping.Wrap; text.TextTrimming = TextTrimming.None; text.VerticalAlignment = VerticalAlignment.Center;
        if (maxWidth is { } width) text.MaxWidth = width;
        SetColumn(text, 1); row.Children.Add(text);
        if (trailing is not null)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetColumn(trailing, 2); row.Children.Add(trailing);
        }
        return new Border { Background = _theme.Layer, BorderBrush = edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, trailing is null ? 12 : 8, 8), Child = row };
    }

    /// <summary>How much of a transcript the placeholder page shows; the loader reads one character more, to know whether it was cut (MainWindow.ShowSavedPreview).</summary>
    internal const int TranscriptPreviewLimit = 120000;

    private static string Bounded(string body) =>
        body.Length > TranscriptPreviewLimit ? body[..TranscriptPreviewLimit] + "\n\n[Preview limited to 120,000 characters; the full imported text remains searchable.]" : body;

    /// <summary>Fills the transcript in once read. A page replaced meanwhile (a rename, a link, the terminal) is left to its own copy.</summary>
    private async Task FillTranscriptAsync(Conversation shown, TextBlock transcript, Func<Task<string?>> loadBody)
    {
        string? body;
        try { body = await loadBody(); }
        catch (Exception exception) { body = "The saved transcript could not be read: " + exception.Message; }
        if (!ReferenceEquals(_previewed, shown)) return;
        _previewed = shown with { Body = body ?? "" };
        // The page may have been rebuilt from the record alone in the meantime (an agent switch at launch); it is built again with the text.
        if (_viewport.Content is ScrollViewer { Content: StackPanel page } && page.Children.Contains(transcript)) transcript.Text = Bounded(body ?? "");
        else RefreshPreview();
    }

    /// <summary>
    /// Runs <paramref name="resume"/>, or, while a resume of this pane is already under way, waits for that one instead. The
    /// start is held as in flight from the request, not only from <see cref="StartAsync"/>: a resume first checks the saved
    /// history, and a second request made meanwhile (a double-click on Resume, or Resume and then a double-click on the
    /// conversation in Sessions) passed the same checks and reached the pane as a second start, refused with a notice.
    /// </summary>
    internal Task<bool> ResumeOnceAsync(Func<Task<bool>> resume) => _resuming.RunAsync(resume);

    /// <summary>
    /// Starts a terminal for <paramref name="origin"/>. A pane that is running, or still starting for an earlier request,
    /// refuses with <see cref="PaneStartedException"/>. A second start used to be let through while the first waited for its
    /// page to load, because nothing runs yet at that point, and it disposed the first request's terminal under it. A start
    /// that fails before its process runs takes its empty terminal away again and puts back the saved conversation, with
    /// what went wrong and the button to try again.
    /// </summary>
    internal async Task StartAsync(TerminalLaunchRequest request, StartRequest origin, Func<bool>? mayStart = null)
    {
        if (IsRunning || _starting)
            throw new PaneStartedException(CurrentStart ?? new StartRequest("an earlier start", DateTimeOffset.UtcNow), IsRunning, Terminal?.ProcessId, Terminal?.ProcessStartedAt);
        _starting = true;
        CurrentStart = origin;
        AgentProcess = null;
        // A Claude Code pane's ID reaches the status line helper, so a reading names the pane it came from (PaneOrigin); any
        // other pane takes the variable away, should SicTerm itself have been started from a pane.
        request = request with
        {
            Environment = new Dictionary<string, string?>(request.Environment ?? new Dictionary<string, string?>())
            {
                [PaneOrigin.PaneVariable] = Descriptor.AgentId == "claude" ? Descriptor.Id : null
            }
        };
        try
        {
            if (Terminal is not null) await Terminal.DisposeAsync();
            var view = Terminal = new TerminalView(_browserDirectory)
            {
                Neighbors = _neighbors, FileDropCaption = Descriptor.AgentId == "shell" ? "Paste path" : "Add to prompt", PetImages = _preferences.PetImages,
                FontFamily = _preferences.FontFamily, FontSize = _preferences.FontSize
            };
            view.Loaded += FocusWhenLoaded;
            ExitedOnRequest = false;
            // A new process starts a new record; whether its tab is selected carries over.
            Presence = new PresenceTracker(Descriptor.AgentId, Presence.IsSelected);
            _turnsHeard = 0;
            void Feed(Action<PresenceTracker, DateTimeOffset> feed) { feed(Presence, DateTimeOffset.UtcNow); PresenceChanged?.Invoke(this); }
            // Typing changes nothing the tab shows, so it is recorded without a repaint. The rest (focus and mouse reports, answers)
            // is recorded too, so the redraw it may bring is not taken for work.
            view.Input += data =>
            {
                if (PresenceTracker.IsUserInput(data)) { Presence.Input(DateTimeOffset.UtcNow); if (!TypedInto) { TypedInto = true; RenderPin(); } }
                else Presence.Sent(DateTimeOffset.UtcNow);
            };
            // A terminal taken away after a failed start still reports its own closing; the pane has moved on.
            view.Exited += code => { if (!ReferenceEquals(Terminal, view)) return; ExitCode = code; SetStatus(PaneStatus.Exited, "Exited · " + code); Feed((tracker, now) => tracker.Exited(now)); };
            view.Failed += exception => { if (ReferenceEquals(Terminal, view)) SetStatus(PaneStatus.Failed, "Error: " + exception.Message); };
            view.Activity += age => Feed((tracker, now) => tracker.Output(now, age));
            view.Bell += () => Feed((tracker, now) => tracker.Bell(now));
            view.TitleChanged += title => Feed((tracker, now) => tracker.Title(title, now));
            view.StatusReported += body => { if (ProgramStatus.Parse(body) is { } status) Feed((tracker, now) => tracker.Report(status, now)); };
            view.ContextMenuRequested += ShowContextMenu;
            view.Shortcut += HandleShortcut;
            view.ProgramCopied += (text, sinceGesture) =>
            {
                if (!ReferenceEquals(Terminal, view)) return;
                try { CopyToClipboard(text); }
                catch (Exception exception) { SetStatus(Status, "Error: " + exception.Message); return; }
                _programCopy = text;
                ProgramCopied?.Invoke(this, text, sinceGesture);
            };
            view.ProgramCopyRefused += length => { if (ReferenceEquals(Terminal, view)) ProgramCopyRefused?.Invoke(this, length); };
            // A drop is pasted as the clipboard is, so a PowerShell pane asks before several lines run here too.
            view.Dropped += async dropped =>
            {
                if (!ReferenceEquals(Terminal, view)) return;
                try { await PasteTextAsync(dropped); }
                catch (Exception exception) { SetStatus(Status, "Error: " + exception.Message); }
            };
            view.SearchResults += (index, count) => _find.SetResults(index, count);
            _viewport.Content = view; PreviewWarning = null;
            RefreshLinkButton();
            SetStatus(PaneStatus.Starting, "Starting");
            // A cancellation means the pane is being closed, or its close was decided while the page loaded: there is nothing
            // to put back, and the close disposes the unstarted terminal.
            try { await view.StartAsync(request, mayStart); await ApplyAppearanceAsync(); }
            catch (Exception exception) when (!view.IsRunning && exception is not OperationCanceledException)
            {
                Terminal = null; CurrentStart = null;
                SetStatus(PaneStatus.Failed, "Error");
                RefreshLinkButton();
                ShowPreview(_previewed, "The terminal did not start: " + exception.Message);
                try { await view.DisposeAsync(); }
                catch (Exception disposal) when (disposal is not OutOfMemoryException) { /* The start's own error is the one to report. */ }
                throw;
            }
            finally
            {
                // A process that runs is shown running even when a step after its launch failed (the focus, the settings).
                if (view.IsRunning && ReferenceEquals(Terminal, view)) SetStatus(PaneStatus.Running, "Running");
            }
        }
        finally { _starting = false; }
    }

    /// <summary>
    /// Re-reads the tracker, and shows "Working…" in the header while the agent works, and "Needs you" while it reports that it
    /// waits for the user (docs/decisions/program-status.md). Only the plain running words are replaced, so "Exiting…" and error
    /// notes stay until their own status clears them. A result unseen out of view (Your turn, Done, Failed) belongs to the tab,
    /// not to the header, which is read only in view.
    /// </summary>
    internal PaneState RefreshPresence(DateTimeOffset now)
    {
        PresenceState = Presence.State(now);
        if (Status == PaneStatus.Running && _state.Text is "Running" or "Working…" or "Needs you")
            _state.Text = PresenceState switch { PaneState.Working => "Working…", PaneState.Blocked => "Needs you", _ => "Running" };
        return PresenceState;
    }

    // How many of the tracker's ended turns the window has heard about; a new process starts a new tracker, and the count again.
    private int _turnsHeard;

    /// <summary>
    /// True once for each turn the user started that has ended since the last call (<see cref="PresenceTracker.TurnsEnded"/>),
    /// as last read by <see cref="RefreshPresence"/>: what rings the ready chime.
    /// </summary>
    internal bool TakeEndedTurn()
    {
        var ended = Presence.TurnsEnded > _turnsHeard;
        _turnsHeard = Presence.TurnsEnded;
        return ended;
    }

    internal async Task ApplyAppearanceAsync()
    {
        // A terminal still loading takes the settings as they are once it is ready (StartAsync applies them). Waiting for it
        // here held a Settings change for as long as the page took, and a page that never loaded threw from the switch.
        if (Terminal is not { } terminal || !terminal.Ready.IsCompletedSuccessfully) return;
        await terminal.ApplyAppearanceAsync(_preferences);
    }

    /// <summary>The least room the folder is shown in: about ten characters of its shortest form, the drive and the start of the folder's name; less is a fragment that says nothing.</summary>
    private const double MinFolderRoom = 72;

    /// <summary>
    /// Shows the whole working folder when the header has room, and shortens it from the middle only as far as the width
    /// requires, so a narrow split pane still shows the folder's own name instead of end-trimming it away. The name comes
    /// first: it may take the whole name-and-folder area, trimmed with an ellipsis, and when what it leaves is under
    /// <see cref="MinFolderRoom"/> the folder is left to the tooltips rather than shown as a fragment.
    /// </summary>
    private void FitPath()
    {
        var room = _titleArea.ActualWidth;
        var available = _pathColumn.ActualWidth;
        var full = System.IO.Path.TrimEndingDirectorySeparator(Descriptor.WorkingDirectory);
        // Three size events reach here per layout pass (the header, the title area, the name), and the panel's slide lays the
        // header out every frame; the same room and folder as last time is the same answer.
        if ((room, available, full) == _lastFit) return;
        _lastFit = (room, available, full);
        if (room > 0)
        {
            var nameRoom = Math.Max(24, Math.Min(180, room));
            if (_name.MaxWidth != nameRoom) _name.MaxWidth = nameRoom;
        }
        var text = available > 0 ? Paths.Fit(full, candidate => PathWidth(candidate) <= available) : full;
        if (_path.Text != text) _path.Text = text;
        _path.Visibility = room > 0 && available < MinFolderRoom ? Visibility.Collapsed : Visibility.Visible;
    }

    private (double Room, double Available, string Path)? _lastFit;
    /// <summary>The measured width of each form of the folder tried so far; the font never changes, so a width stands for the pane's life.</summary>
    private readonly Dictionary<string, double> _pathWidths = new(StringComparer.Ordinal);

    private double PathWidth(string text)
    {
        if (_pathWidths.TryGetValue(text, out var width)) return width;
        _pathMeasure.Text = text;
        _pathMeasure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        width = _pathMeasure.DesiredSize.Width;
        if (_pathWidths.Count > 256) _pathWidths.Clear();
        _pathWidths[text] = width;
        return width;
    }

    /// <summary>
    /// Verification: the header path is the whole folder if that fits, otherwise a shortened form that fits and ends in the
    /// folder's name; and it is hidden only when the name leaves it less room than <see cref="MinFolderRoom"/>.
    /// </summary>
    internal bool PathFitsBest()
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(Descriptor.WorkingDirectory);
        var available = _pathColumn.ActualWidth;
        if (_titleArea.ActualWidth <= 0) return false;
        if (_path.Visibility == Visibility.Collapsed) return available < MinFolderRoom;
        if (available <= 0) return false;
        if (PathWidth(full) <= available) return _path.Text == full;
        return _path.Text.EndsWith(Paths.Leaf(full), StringComparison.Ordinal) && (PathWidth(_path.Text) <= available || _path.Text == Paths.Shorten(full, 1));
    }

    internal void UpdateIdentity(SavedPane descriptor)
    {
        if (descriptor.SessionId != Descriptor.SessionId || descriptor.CreationName != Descriptor.CreationName) HistoryMissing = null;
        Descriptor = descriptor;
        RefreshName();
        RefreshFolderMark();
        FitPath();
        ToolTipService.SetToolTip(_path, descriptor.WorkingDirectory);
        _tileHost.Child = _theme.AgentTile(descriptor.AgentId, 14);
        RefreshLinkButton();
        UpdateFolderButton();
        RenderPin();
    }

    /// <summary>The pane's working folder when it exists on disk, otherwise null; what the folder button opens.</summary>
    internal string? FolderToOpen => Descriptor.WorkingDirectory is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;
    /// <summary>False when the pane has no working folder; the folder button and menu item are disabled.</summary>
    internal bool CanOpenFolder => _folderButton.IsEnabled;

    private void UpdateFolderButton()
    {
        // The glyph carries its own ink, which the button's disabled state does not dim.
        var known = Descriptor.WorkingDirectory.Length > 0;
        _folderButton.IsEnabled = known; _folderButton.Opacity = known ? 1 : 0.4;
    }

    /// <summary>Opens the working folder in File Explorer, so an agent's output files are one click away.</summary>
    internal void OpenFolder()
    {
        if (FolderToOpen is not { } folder) { SetStatus(Status, "Folder not found: " + Descriptor.WorkingDirectory); return; }
        try { Paths.OpenInExplorer(folder); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { SetStatus(Status, "Error: " + exception.Message); }
    }

    internal void OpenFind()
    {
        if (Terminal is null) return;
        _find.Open();
    }

    private async void HandleShortcut(string name, string? text)
    {
        try
        {
            switch (name)
            {
                case "find": OpenFind(); break;
                case "copy": if (!string.IsNullOrEmpty(text)) CopyToClipboard(text); break;
                case "paste": await PasteFromClipboardAsync(asPath: false); break;
                case "paste-or-key":
                    if (!await PasteFromClipboardAsync(asPath: false) && Terminal is not null) await Terminal.WriteAsync("\x16");
                    break;
                default: Shortcut?.Invoke(name); break;
            }
        }
        catch (Exception exception) { SetStatus(Status, "Error: " + exception.Message); }
    }

    private void ShowContextMenu(TerminalContextMenuRequest request)
    {
        if (Terminal is null) return;
        var terminal = Terminal;
        var menu = new MenuFlyout();
        // A terminal selection first; else, after a drag the program took and copied itself (Claude Code, Codex), that copy.
        var programCopy = !request.HasSelection && request.ProgramCopied ? _programCopy : null;
        var canCopy = request.HasSelection || programCopy is not null;
        AddItem(menu, canCopy || !request.MouseReporting ? "Copy" : "Copy (Shift+drag to select)", "", canCopy, async () =>
        {
            var text = programCopy ?? await terminal.GetSelectionAsync();
            if (text.Length > 0) CopyToClipboard(text);
            await terminal.FocusInputAsync();
        });
        AddItem(menu, "Paste", "", true, () => PasteFromClipboardAsync(asPath: false));
        AddItem(menu, "Paste as path", "", true, () => PasteFromClipboardAsync(asPath: true));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu, "Find…", "", true, () => { OpenFind(); return Task.CompletedTask; });
        AddItem(menu, "Clear scrollback", "", true, async () => { await terminal.ClearAsync(); await terminal.FocusInputAsync(); });
        AddItem(menu, "Open folder in File Explorer", "", CanOpenFolder, () => { OpenFolder(); return Task.CompletedTask; }, "Ctrl+Shift+E");
        menu.Items.Add(new MenuFlyoutSeparator());
        AddPaneItems(menu);
        menu.Closed += async (_, _) => { if (IsRunning && Editor is null) await terminal.FocusInputAsync(); };
        menu.ShowAt(terminal, new FlyoutShowOptions { Position = new Windows.Foundation.Point(request.X, request.Y) });
    }

    /// <summary>
    /// The pane's menu from its header, or from the placeholder page while no terminal runs: the folder, then the items that act
    /// on the pane (<see cref="AddPaneItems"/>), so a pane that is not running has them too. A right-click in a live terminal gets
    /// the terminal's own menu, which carries the same items after its copy, paste and find.
    /// </summary>
    private void ShowPaneMenu(FrameworkElement at, Windows.Foundation.Point? position)
    {
        if (Editor is not null) return;
        var menu = new MenuFlyout();
        AddItem(menu, "Open folder in File Explorer", "", CanOpenFolder, () => { OpenFolder(); return Task.CompletedTask; }, "Ctrl+Shift+E");
        menu.Items.Add(new MenuFlyoutSeparator());
        AddPaneItems(menu);
        menu.ShowAt(at, position is { } point ? new FlyoutShowOptions { Position = point } : new FlyoutShowOptions());
    }

    /// <summary>
    /// The items that act on the pane itself, on the terminal's menu and the header's alike: rename, the splits, the moves, a new
    /// session in the folder, the pins and the close. Pin pane puts the pane's conversation at the top of Sessions and reads Unpin
    /// pane while it is there; Pin and close pane does the same and closes this pane alone. Both are greyed for a pane with nothing
    /// saved to pin: a shell, an agent pane not yet linked, or a new session nothing has been typed into (<see cref="CanPin"/>).
    /// </summary>
    private void AddPaneItems(MenuFlyout menu)
    {
        AddItem(menu, "Rename pane…", "", true, () => { BeginRename(); return Task.CompletedTask; }, "F2");
        var right = SplitItem(PaneSplit.Columns);
        var down = SplitItem(PaneSplit.Rows);
        RefreshSplitItems(right, down);
        menu.Items.Add(right); menu.Items.Add(down); menu.Items.Add(KeepEvenItem());
        AddItem(menu, IsZoomed ? "Zoom out" : "Zoom pane", IsZoomed ? "" : "", true, () => { ZoomRequested?.Invoke(this); return Task.CompletedTask; }, "Ctrl+Shift+Z");
        AddItem(menu, "Move pane to new tab", "", CanMoveOut, () => { MoveOutRequested?.Invoke(this); return Task.CompletedTask; });
        ExtendingMenu?.Invoke(this, menu);
        if (!AgentOff) AddItem(menu, "New " + Theme.AgentName(Descriptor.AgentId) + " session in this folder", "", true, () => { DuplicateRequested?.Invoke(this); return Task.CompletedTask; });
        var pinnable = CanPin;
        var pinned = pinnable && IsPinned;
        AddItem(menu, pinned ? "Unpin pane" : "Pin pane", pinned ? Theme.UnpinGlyph : Theme.PinGlyph, pinnable, () => { PinRequested?.Invoke(this); return Task.CompletedTask; });
        AddItem(menu, "Pin and close pane", Theme.PinGlyph, pinnable, () => { PinAndCloseRequested?.Invoke(this); return Task.CompletedTask; });
        AddItem(menu, "Close pane", "", true, () => { CloseRequested?.Invoke(this); return Task.CompletedTask; });
    }

    /// <summary>A menu item whose click reports its own failure in the header, since an exception leaving a handler ends SicTerm.</summary>
    private MenuFlyoutItem AddItem(MenuFlyout menu, string text, string glyph, bool enabled, Func<Task> action, string? accelerator = null)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontFamily = Theme.Icons }, IsEnabled = enabled };
        if (accelerator is not null) item.KeyboardAcceleratorTextOverride = accelerator;
        item.Click += async (_, _) => { try { await action(); } catch (Exception exception) { SetStatus(Status, "Error: " + exception.Message); } };
        menu.Items.Add(item);
        return item;
    }

    /// <summary>Puts the text on the clipboard, waiting out another program holding it (<see cref="ClipboardWrite"/>); only a copy that landed is recorded.</summary>
    private void CopyToClipboard(string text)
    {
        ClipboardWrite.SetText(text);
        LastCopied = text;
    }

    /// <summary>
    /// False when the clipboard had no text or files to paste. Read with the Win32 calls (<see cref="Win32Clipboard"/>), not the
    /// WinRT view: in a Remote Desktop session that view answered text copied on the local PC with nothing, until another program
    /// had fetched it. Throws, to the status line, when another program held the clipboard for seconds.
    /// </summary>
    private async Task<bool> PasteFromClipboardAsync(bool asPath)
    {
        if (Terminal is not { } terminal) return false;
        var content = await Win32Clipboard.ReadAsync();
        string? text = null;
        if (content.Files.Count > 0) text = PathPaste.Join(content.Files);
        else if (content.Text is { } copied) text = asPath ? PathPaste.Quote(copied.Trim()) : copied;
        if (string.IsNullOrEmpty(text)) { await terminal.FocusInputAsync(); return false; }
        await PasteTextAsync(text);
        return true;
    }

    /// <summary>
    /// Pastes text into the terminal, from the clipboard or a drop, and gives the terminal the keyboard. Control characters go
    /// (<see cref="ClipboardText.ForPaste"/>). A PowerShell pane runs each pasted line as it arrives, so there, as in Windows
    /// Terminal, one line loses its trailing whitespace and with it a line break that would run it, and text of several lines is
    /// shown first and sent only on Paste while Settings → Terminal → Confirm multi-line pastes into PowerShell is on. That is asked
    /// whatever the terminal's bracketed paste mode says, since anything a pane prints can switch the mode on in a shell that does
    /// not take it (Windows Terminal's GH#13014). An agent takes a paste into its prompt, and its pane is not asked.
    /// </summary>
    internal async Task PasteTextAsync(string text)
    {
        if (Terminal is not { } terminal) return;
        var paste = ClipboardText.ForPaste(text);
        if (Descriptor.AgentId == "shell")
        {
            paste = ClipboardText.TrimSingleLine(paste);
            if (_preferences.ConfirmMultiLinePaste && ClipboardText.IsMultiLine(paste) && !await _confirmPaste(paste)) paste = "";
        }
        // The dialog may have been open while the pane's terminal was replaced; a paste is for the terminal it was asked of.
        if (paste.Length > 0 && ReferenceEquals(Terminal, terminal)) await terminal.PasteAsync(paste);
        await terminal.FocusInputAsync();
    }

    /// <summary>
    /// Asks the agent to end its own session and waits up to the budget. False when it is still running afterwards or has no
    /// exit command. A request made while one is under way (a Quit or a Windows logoff during a tab or pane close, or a tab
    /// closed while this pane is closing) waits for that one instead of typing the command again; a repeat close of the same
    /// tab or pane is turned away earlier, by the window's closing guards.
    /// </summary>
    internal Task<bool> TryExitAsync(TimeSpan budget) => _exit.RunAsync(() => SendExitAsync(budget));

    /// <summary>True once the agent ended within the budget after being asked to exit, for verification.</summary>
    internal bool ExitedOnRequest { get; private set; }

    /// <summary>Set while a decided close of this pane waits for its agent; the layout no longer saves the pane.</summary>
    internal bool Closing { get; set; }

    private async Task<bool> SendExitAsync(TimeSpan budget)
    {
        if (Terminal is not { } terminal || !IsRunning) return true;
        if (AgentExit.For(Descriptor.AgentId) is not { } exit) return false;
        SetStatus(PaneStatus.Running, "Exiting…");
        bool exited;
        try { exited = await exit.SendAsync(terminal.WriteAsync, terminal.WaitForExitAsync, budget); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException) { return !IsRunning; }
        if (!exited && IsRunning) SetStatus(PaneStatus.Running, "Running");
        ExitedOnRequest = exited;
        return exited;
    }
    private void SetStatus(PaneStatus status, string text)
    {
        Status = status; _state.Text = text;
        _dot.Fill = status switch { PaneStatus.Running => _chrome.HeaderSuccess, PaneStatus.Failed => _theme.Danger, PaneStatus.Starting => _chrome.HeaderWarning, _ => _theme.Neutral };
        StatusChanged?.Invoke();
    }

    public async ValueTask DisposeAsync() { if (Terminal is not null) await Terminal.DisposeAsync(); }
}
