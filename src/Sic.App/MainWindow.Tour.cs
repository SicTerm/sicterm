using Sic.Core.Limits;
using Sic.Core.Sessions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace Sic.App;

/// <summary>
/// The welcome tour (docs/decisions/first-run.md): eight stops over the live window. A dimmed layer lies over everything with
/// a hole cut round one real control at a time and a caption beside it; some stops act for real on the way in: Sessions and
/// Workspaces opened and the current workspace's row lit, the latest conversation opened as a
/// placeholder (nothing starts), the drop mark flashed over a pane, and sample meters painted in the status bar and a pane
/// header for the length of the stop. Esc leaves; Right or Enter goes on, Left back. It counts as an open dialog, so the
/// window's drag areas stand down while it runs.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>One stop: the caption, the control the hole is cut round (null for the caption alone), and what happens on the way in and out.</summary>
    private sealed record TourStop(string Title, string Text, Func<FrameworkElement?> Target, Func<Task>? Enter = null, Action? Leave = null, string? Next = null);

    private const double TourTipWidth = 360, TourPad = 4, TourGap = 14, TourMargin = 12;
    private static readonly TimeSpan TourHoleMotion = TimeSpan.FromMilliseconds(280);
    private readonly Grid _tourLayer = new() { Visibility = Visibility.Collapsed };
    private readonly Canvas _tourCanvas = new();
    private readonly Rectangle[] _tourShade = Enumerable.Range(0, 4).Select(_ => new Rectangle()).ToArray();
    private readonly Border _tourFrame = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
    private Border _tourTip = null!;
    private TextBlock _tourCount = null!, _tourTitle = null!, _tourText = null!;
    private Button _tourNext = null!, _tourBack = null!, _tourSkip = null!;
    private List<TourStop> _tourStops = [];
    private int _tourIndex = -1;
    private Rect _tourHole, _tourHoleFrom, _tourWanted;
    private DateTimeOffset _tourHoleSince;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _tourTimer;
    private bool _tourBusy, _tourBuilt, _tourOpenedTab;
    private SessionPane? _tourContextPane;
    private ContextReading? _tourContextKept;

    internal bool TourRunning => _tourIndex >= 0;
    /// <summary>The stop showing, from 0, or −1; for verification.</summary>
    internal int TourStep => _tourIndex;
    internal int TourStepCount => _tourStops.Count;
    internal string TourStepTitle => TourRunning ? _tourStops[_tourIndex].Title : "";

    private void BuildTourLayer()
    {
        if (_tourBuilt) return;
        _tourBuilt = true;
        var shade = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0));
        foreach (var rect in _tourShade) { rect.Fill = shade; _tourCanvas.Children.Add(rect); }
        _tourFrame.BorderBrush = _theme.Accent;
        _tourCanvas.Children.Add(_tourFrame);
        var stack = new StackPanel { Spacing = 6 };
        _tourCount = _theme.Eyebrow(""); _tourCount.Foreground = _theme.TextSecondary;
        _tourTitle = _theme.Heading("", 17); _tourTitle.TextWrapping = TextWrapping.Wrap; _tourTitle.TextTrimming = TextTrimming.None; _displayText.Add(_tourTitle);
        _tourText = _theme.BodyText("", 13.5); _tourText.TextWrapping = TextWrapping.Wrap; _tourText.TextTrimming = TextTrimming.None; _tourText.Margin = new Thickness(0, 2, 0, 8);
        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _tourNext = _theme.PrimaryButton("Next"); _tourNext.Click += async (_, _) => await TourNextAsync();
        _tourBack = Theme.TextButton("Back"); _tourBack.Click += async (_, _) => await TourBackAsync();
        _tourSkip = Theme.TextButton("Skip tour"); _tourSkip.HorizontalAlignment = HorizontalAlignment.Right; _tourSkip.Click += (_, _) => EndTour();
        Grid.SetColumn(_tourBack, 1); Grid.SetColumn(_tourSkip, 2);
        buttons.Children.Add(_tourNext); buttons.Children.Add(_tourBack); buttons.Children.Add(_tourSkip);
        stack.Children.Add(_tourCount); stack.Children.Add(_tourTitle); stack.Children.Add(_tourText); stack.Children.Add(buttons);
        _tourTip = new Border
        {
            Width = TourTipWidth, Background = new SolidColorBrush(Theme.Over(_theme.Panel.Color, _theme.Base.Color)), BorderBrush = _theme.StrokeStrong,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 14, 18, 14), Child = stack
        };
        _theme.Accentuate(_tourTip.Resources);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_tourTip, "Welcome tour");
        _tourCanvas.Children.Add(_tourTip);
        _tourLayer.Children.Add(_tourCanvas);
        _tourLayer.KeyDown += (_, args) =>
        {
            if (!TourRunning) return;
            switch (args.Key)
            {
                case VirtualKey.Escape: args.Handled = true; EndTour(); break;
                case VirtualKey.Right: args.Handled = true; _ = TourNextAsync(); break;
                case VirtualKey.Left: args.Handled = true; _ = TourBackAsync(); break;
            }
        };
        Grid.SetRowSpan(_tourLayer, 2);
        Canvas.SetZIndex(_tourLayer, 100);
        _root.Children.Add(_tourLayer);
    }

    /// <summary>Starts the tour at its first stop; nothing happens while one runs or the window is closing.</summary>
    internal async Task StartTourAsync()
    {
        if (TourRunning || _closing) return;
        BuildTourLayer();
        _tourStops = BuildTourStops();
        _tourOpenedTab = false;
        _dialogsOpen++; UpdateDragRegions();
        _tourLayer.Visibility = Visibility.Visible;
        _tourHole = _tourHoleFrom = _tourWanted = RootCentre();
        _tourHoleSince = DateTimeOffset.UtcNow;
        if (_tourTimer is null)
        {
            _tourTimer = DispatcherQueue.CreateTimer();
            _tourTimer.Interval = TimeSpan.FromMilliseconds(33);
            _tourTimer.Tick += (_, _) => PlaceTour();
        }
        _tourTimer.Start();
        await ShowTourStopAsync(0);
    }

    internal async Task TourNextAsync() { if (TourRunning && !_tourBusy) await ShowTourStopAsync(_tourIndex + 1); }
    private async Task TourBackAsync() { if (TourRunning && !_tourBusy && _tourIndex > 0) await ShowTourStopAsync(_tourIndex - 1); }

    private async Task ShowTourStopAsync(int index)
    {
        if (index < 0 || index >= _tourStops.Count) { EndTour(); return; }
        if (TourRunning) RunTourLeave(_tourStops[_tourIndex]);
        _tourIndex = index;
        var stop = _tourStops[index];
        var last = index == _tourStops.Count - 1;
        _tourCount.Text = $"{index + 1} of {_tourStops.Count}";
        _tourTitle.Text = stop.Title;
        _tourText.Text = stop.Text + (last && _tourOpenedTab ? " The conversation the tour opened is a placeholder: Resume when ready, or close its tab." : "");
        _tourNext.Content = stop.Next ?? (last ? "Finish" : "Next");
        _tourBack.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        _tourBusy = true; _tourNext.IsEnabled = false;
        try { if (stop.Enter is { } enter) await enter(); }
        catch (Exception exception) { Diagnostics.Log("tour stop " + stop.Title + ": " + exception.Message); }
        finally { _tourBusy = false; _tourNext.IsEnabled = true; }
        if (_tourIndex != index) return;
        _tourHoleFrom = _tourHole; _tourHoleSince = DateTimeOffset.UtcNow; _tourWanted = TourTargetBounds() ?? RootCentre();
        PlaceTour();
        _tourNext.Focus(FocusState.Programmatic);
    }

    private void RunTourLeave(TourStop stop)
    {
        try { stop.Leave?.Invoke(); }
        catch (Exception exception) { Diagnostics.Log("tour leaving " + stop.Title + ": " + exception.Message); }
    }

    /// <summary>Ends the tour wherever it is, undoing the stop's own painting, and gives the keyboard back to the pane.</summary>
    internal void EndTour()
    {
        if (!TourRunning) return;
        var stop = _tourStops[_tourIndex];
        _tourIndex = -1;
        RunTourLeave(stop);
        _tourTimer?.Stop();
        _tourLayer.Visibility = Visibility.Collapsed;
        _dialogsOpen = Math.Max(0, _dialogsOpen - 1); UpdateDragRegions();
        if (_activePane is { } pane) pane.FocusInput();
        ReturnToWelcomeCard();
    }

    private Rect RootCentre() => new(_root.ActualWidth / 2, _root.ActualHeight / 2, 0, 0);

    /// <summary>The stop's control, padded, in the window's coordinates, or null while it has no place on screen.</summary>
    private Rect? TourTargetBounds()
    {
        if (!TourRunning) return null;
        FrameworkElement? target;
        try { target = _tourStops[_tourIndex].Target(); } catch (Exception) { return null; }
        if (target is null || !target.IsLoaded || target.Visibility == Visibility.Collapsed || target.ActualWidth <= 0 || target.ActualHeight <= 0) return null;
        try
        {
            var bounds = target.TransformToVisual(_root).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            return new Rect(bounds.X - TourPad, bounds.Y - TourPad, bounds.Width + 2 * TourPad, bounds.Height + 2 * TourPad);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Lays the shade, the frame and the caption out for the stop: the hole moves to its control over a moment, and follows it
    /// afterwards (a panel sliding open, the window resized); the caption sits to the right of the hole, else left, below, above,
    /// or over its centre when the control is large or there is none.
    /// </summary>
    private void PlaceTour()
    {
        if (!TourRunning) return;
        var wanted = TourTargetBounds() ?? RootCentre();
        if (wanted != _tourWanted) { _tourHoleFrom = _tourHole; _tourHoleSince = DateTimeOffset.UtcNow; _tourWanted = wanted; }
        var t = Math.Clamp((DateTimeOffset.UtcNow - _tourHoleSince) / TourHoleMotion, 0, 1);
        var ease = 1 - Math.Pow(1 - t, 3);
        var hole = _tourHole = new Rect(Lerp(_tourHoleFrom.X, wanted.X, ease), Lerp(_tourHoleFrom.Y, wanted.Y, ease), Lerp(_tourHoleFrom.Width, wanted.Width, ease), Lerp(_tourHoleFrom.Height, wanted.Height, ease));
        var width = _root.ActualWidth; var height = _root.ActualHeight;
        PlaceOnCanvas(_tourShade[0], 0, 0, width, hole.Top);
        PlaceOnCanvas(_tourShade[1], 0, hole.Bottom, width, height - hole.Bottom);
        PlaceOnCanvas(_tourShade[2], 0, hole.Top, hole.Left, hole.Height);
        PlaceOnCanvas(_tourShade[3], hole.Right, hole.Top, width - hole.Right, hole.Height);
        PlaceOnCanvas(_tourFrame, hole.X, hole.Y, hole.Width, hole.Height);
        _tourFrame.Visibility = hole.Width > 1 ? Visibility.Visible : Visibility.Collapsed;
        // The caption's height follows its text: measured with its width alone, since an explicit height would be what every later measure returned.
        _tourTip.Measure(new Size(TourTipWidth, double.PositiveInfinity));
        var tipHeight = _tourTip.DesiredSize.Height;
        var hasHole = wanted.Width > 0;
        var lowTarget = hasHole && wanted.Top > height * 0.75;
        double x, y;
        if (lowTarget && wanted.Top - TourGap - tipHeight >= TourMargin) { x = wanted.Right - TourTipWidth; y = wanted.Top - TourGap - tipHeight; }
        else if (hasHole && wanted.Right + TourGap + TourTipWidth <= width - TourMargin) { x = wanted.Right + TourGap; y = wanted.Top; }
        else if (hasHole && wanted.Left - TourGap - TourTipWidth >= TourMargin) { x = wanted.Left - TourGap - TourTipWidth; y = wanted.Top; }
        else if (hasHole && wanted.Bottom + TourGap + tipHeight <= height - TourMargin) { x = wanted.Left; y = wanted.Bottom + TourGap; }
        else if (hasHole && wanted.Top - TourGap - tipHeight >= TourMargin) { x = wanted.Left; y = wanted.Top - TourGap - tipHeight; }
        else { x = wanted.X + wanted.Width / 2 - TourTipWidth / 2; y = wanted.Y + wanted.Height / 2 - tipHeight / 2; }
        x = Math.Clamp(x, TourMargin, Math.Max(TourMargin, width - TourTipWidth - TourMargin));
        y = Math.Clamp(y, TourMargin, Math.Max(TourMargin, height - tipHeight - TourMargin));
        if (Canvas.GetLeft(_tourTip) != x) Canvas.SetLeft(_tourTip, x);
        if (Canvas.GetTop(_tourTip) != y) Canvas.SetTop(_tourTip, y);
    }

    private static void PlaceOnCanvas(FrameworkElement element, double x, double y, double width, double height)
    {
        width = Math.Max(0, width); height = Math.Max(0, height);
        if (Canvas.GetLeft(element) != x) Canvas.SetLeft(element, x);
        if (Canvas.GetTop(element) != y) Canvas.SetTop(element, y);
        if (element.Width != width) element.Width = width;
        if (element.Height != height) element.Height = height;
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private List<TourStop> BuildTourStops()
    {
        // Wording reviewed with Chris, November 27, 2026. On a PC with no tab open, the stops about tabs and panes point at nothing
        // rather than at the nearest control (they had framed New session and the home screen), and sit in the middle.
        var agent = _detection.Values.Any(found => found.Available);
        var folders = Folders.Count;
        return
        [
            new("Any agent, any folder",
                "New session starts an agent or PowerShell in any folder. Ctrl+Shift+T opens it from anywhere, and File Explorer's right-click menu has Open in SicTerm.",
                () => _railItems["new"].Button),
            new("Every saved conversation",
                "Sessions lists every saved conversation on this PC, read from the agents' own histories. Ctrl+Shift+K searches titles, folders and text. A double-click resumes one where it left off; its pin keeps it at the top.",
                () => _history,
                Enter: async () => { OpenPanel("sessions", persist: false); await Task.Delay(320); PulseFirstRowPin(); }),
            new("Folders and workspaces",
                "Each listed folder is a workspace with its own tabs. This list switches between them, as does Ctrl+Shift+A. The Folders panel adds folders and puts them in order.",
                () => _workspaceRows.TryGetValue(_workspace.Id, out var view) ? view.Row : _workspaceList,
                Enter: async () =>
                {
                    OpenPanel("workspaces", persist: false); await Task.Delay(320);
                    if (_workspaceRows.TryGetValue(_workspace.Id, out var view)) _ = PulseAsync(_tourIndex, view.Fold);
                }),
            new("A tab, resumed or waiting",
                "A tab is one conversation. After a restart each tab comes back paused, with its transcript, and nothing runs until Resume. F5 resumes them all.",
                () => _open.Count > 0 ? _tabs : null,
                Enter: async () =>
                {
                    if (_open.Count > 0 || LatestConversation() is not { } latest) return;
                    _tourOpenedTab = await OpenConversationAsync(latest) is not null;
                    await Task.Delay(250);
                }),
            new("Split, drag, tile",
                "A pane's header splits it right or down, up to 16 panes per tab. Dragging a tab or a pane's header moves it, and the accent mark shows where it lands. F6 gathers tabs in the same folder into one.",
                () => SelectedHost is { Panes.Count: > 0 } host ? FocusedPane(host).SplitButton : null,
                Enter: async () => { await Task.Delay(120); ShowTourDropMark(); },
                Leave: () => SelectedHost?.Content.HideDrop()),
            new("Limits and context, in view",
                "While an agent runs, its plan's 5-hour and weekly limits show here, colored by pace. Each pane's header shows the model and how full its context is. SicTerm reads this from the agents' own files, never a credential. Settings → Agents turns it off.",
                () => _limitsPanel,
                Enter: async () => { PreviewLimits(true); await Task.Delay(120); },
                Leave: () => PreviewLimits(false)),
            new("Full screen, and every key",
                "F11 goes full screen, and F1 lists every shortcut. Closing the window keeps sessions running in the tray; Quit asks each agent to exit cleanly first.",
                () => _open.Count > 0 ? _tabs : null),
            // The last stop says what to do next on this PC (Chris: "make sure they know what they need to do next").
            !agent
                ? new("Next: install a coding agent",
                    "\"Get set up\" installs one. Then New session (Ctrl+Shift+T) starts it in any folder.",
                    () => null, Next: "Done")
                : folders == 0
                    ? new("Next: add folders",
                        "\"Add folders\" lists the folders used most, ready to tick. Then New session (Ctrl+Shift+T) starts an agent in any of them.",
                        () => null, Next: "Done")
                    : new("Ready",
                        "New session (Ctrl+Shift+T) starts an agent in any folder. Ctrl+Shift+K finds any saved conversation, and F5 resumes everything waiting after a restart.",
                        () => null, Next: "Done"),
        ];
    }

    private Conversation? LatestConversation()
    {
        try { return _catalog.Search("", limit: 1, aliases: _preferences.ConversationNames, hiddenAgents: HiddenAgents()).FirstOrDefault(); }
        catch (Exception exception) { Diagnostics.Log("tour: " + exception.Message); return null; }
    }

    private void PulseFirstRowPin()
    {
        var row = _history.Items.OfType<ListViewItem>().FirstOrDefault(item => item.IsEnabled);
        if (row is not null && RowPin(row) is { } pin) _ = PulseAsync(_tourIndex, pin);
    }

    /// <summary>Lights each control in turn, in the accent's tint, for as long as the stop shows.</summary>
    private async Task PulseAsync(int step, params Control[] controls)
    {
        foreach (var control in controls)
        {
            if (_tourIndex != step) return;
            var opacity = control.Opacity; var background = control.Background;
            control.Opacity = 1; control.Background = _theme.AccentSoft;
            await Task.Delay(700);
            control.Background = background; control.Opacity = opacity;
            await Task.Delay(120);
        }
    }

    /// <summary>The drop mark over the right half of the focused pane, as a tab dragged there would show it.</summary>
    private void ShowTourDropMark()
    {
        if (SelectedHost is not { Panes.Count: > 0 } host) return;
        var pane = FocusedPane(host);
        var canvas = host.Content;
        try
        {
            var bounds = pane.TransformToVisual(canvas).TransformBounds(new Rect(0, 0, pane.ActualWidth, pane.ActualHeight));
            if (canvas.DropAt(new Point(bounds.X + bounds.Width * 0.8, bounds.Y + bounds.Height / 2)) is { } drop) canvas.ShowDrop(drop.Area, "A tab dragged here", allowed: true);
        }
        catch (Exception exception) { Diagnostics.Log("tour drop mark: " + exception.Message); }
    }

    /// <summary>Whether the tour's drop mark is up; for verification.</summary>
    internal bool TourDropMarkShown => SelectedHost?.Content.DropShown is not null;

    /// <summary>
    /// Sample plan meters in the status bar, one group for Claude and one for ChatGPT, and a sample model and context meter
    /// in the focused pane's header, for the stop that explains them; off, the real readings come back.
    /// </summary>
    private void PreviewLimits(bool on)
    {
        if (on)
        {
            var now = DateTimeOffset.Now;
            _limitsPanel.Children.Clear();
            void Group(string plan, string agent, double shortUsed, double longUsed)
            {
                _limitsPanel.Children.Add(new Border { Width = 1, Height = 14, Background = _theme.LimitDivider, VerticalAlignment = VerticalAlignment.Center });
                var reading = new LimitReading(new LimitWindow(shortUsed, now.AddHours(2), TimeSpan.FromHours(5)), new LimitWindow(longUsed, now.AddDays(3), TimeSpan.FromDays(7)), now);
                _limitsPanel.Children.Add(LimitGroup(plan, [agent], reading, now));
            }
            Group(ClaudePlan, "claude", 58, 84);
            Group(ChatGptPlanId, "codex", 23, 40);
            if (SelectedHost is { Panes.Count: > 0 } host)
            {
                _tourContextPane = FocusedPane(host);
                _tourContextKept = _tourContextPane.Context;
                _tourContextPane.ShowContext(new ContextReading(_tourContextPane.Descriptor.SessionId ?? _tourContextPane.Descriptor.Id, 72, 200_000, 144_000, "Opus 4.5", now));
            }
        }
        else
        {
            RenderLimits();
            if (_tourContextPane is { } pane) { pane.ShowContext(_tourContextKept); _tourContextPane = null; _tourContextKept = null; }
        }
    }

    /// <summary>Whether the sample meters are up; for verification.</summary>
    internal bool TourLimitsPreviewed => _limitsPanel.Children.OfType<StackPanel>().Any(group => group.Tag is ChatGptPlanId);
}
