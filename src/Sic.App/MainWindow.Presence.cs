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

/// <summary>What the tabs show of their panes: the status dots and their blink, the tab mark, each tab's presence, and the ready chime.</summary>
internal sealed partial class MainWindow
{
    // The dots that want the user blink together on one timer at the terminal cursor's cadence (StateDots.BlinkHalf), always, as
    // the working ring spins; off the beat they dim to StateDots.BlinkDim rather than vanish, so the state stays readable.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _blinkTimer;
    private bool _blinkOffBeat;
    /// <summary>Whether any dot is blinking: a tab out of view wants the user or holds an unseen result. For verification.</summary>
    internal bool Blinking => _blinkTimer is { IsRunning: true };

    /// <summary>
    /// Whether the tab's dot blinks: Your turn and Done are only ever shown out of view and blink; Needs you and Failed, which the
    /// agent itself reported, show in view too, where they hold still, since nothing blinks beside the terminal being read
    /// (docs/decisions/state-dots.md, docs/decisions/program-status.md).
    /// </summary>
    private bool Blinks(TabHost host) => host.Presence switch
    {
        PaneState.Attention or PaneState.Done => true,
        PaneState.Blocked or PaneState.Error => !(ReferenceEquals(host, SelectedHost) && WindowInView),
        _ => false
    };

    /// <summary>Starts or stops the blink as the tabs stand: called whenever a tab's dot is repainted or the status is updated.</summary>
    private void RefreshBlink()
    {
        var wanted = _open.Values.Any(host => Blinks(host) && host.Dot.Visibility == Visibility.Visible);
        if (!wanted)
        {
            _blinkTimer?.Stop();
            _blinkOffBeat = false;
            foreach (var host in _open.Values) host.Dot.Opacity = 1;
            RefreshSwitcherStates();
            return;
        }
        if (_blinkTimer is null)
        {
            _blinkTimer = DispatcherQueue.CreateTimer();
            _blinkTimer.Interval = StateDots.BlinkHalf;
            _blinkTimer.Tick += (_, _) => { _blinkOffBeat = !_blinkOffBeat; ApplyBlink(); };
        }
        if (!_blinkTimer.IsRunning) _blinkTimer.Start();
        ApplyBlink();
    }

    /// <summary>Every blinking dot on the beat or off it, in step; any other dot at full opacity.</summary>
    private void ApplyBlink()
    {
        foreach (var host in _open.Values) host.Dot.Opacity = _blinkOffBeat && Blinks(host) ? StateDots.BlinkDim : 1;
        // The open workspace switcher's dots blink on the same beat.
        RefreshSwitcherStates();
    }

    /// <summary>What a blocked agent in the tab waits for, as its pane reported it, in the tooltip's words; null when it did not say.</summary>
    private static string? BlockedKind(TabHost host) => host.Panes.Select(pane => pane.Presence.BlockedKind).FirstOrDefault(kind => kind is not null) switch
    {
        "auth" => "sign-in",
        var kind => kind
    };

    private void RefreshTabStatus(TabHost host)
    {
        // A tab between its panes leaving and its own removal (a move) has nothing to show.
        if (host.Panes.Count == 0) return;
        var running = host.Panes.Any(pane => pane.IsRunning);
        var failed = host.Panes.Any(pane => pane.Status == PaneStatus.Failed);
        var exited = host.Panes.Any(pane => pane.Status == PaneStatus.Exited);
        var starting = host.Panes.Any(pane => pane.Status == PaneStatus.Starting);
        var presence = host.Presence;
        // The user is wanted, or a result waits unseen: the tab says so in bold, and its dot takes the state's hue (docs/decisions/program-status.md).
        var wanted = presence is PaneState.Attention or PaneState.Blocked or PaneState.Done or PaneState.Error;
        var working = presence == PaneState.Working;
        var spinning = working;
        host.Ring.IsActive = spinning;
        host.Ring.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;
        host.Dot.Visibility = !spinning && (running || failed || exited || starting || wanted) ? Visibility.Visible : Visibility.Collapsed;
        host.Dot.Fill = presence switch
        {
            PaneState.Blocked or PaneState.Error => _theme.DangerDot,
            PaneState.Attention or PaneState.Done => _theme.WarningDot,
            _ => working ? _theme.Accent : running ? _theme.SuccessDot : failed ? _theme.Danger : starting ? _theme.WarningDot : _theme.Neutral
        };
        RefreshBlink();
        host.Title.FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(wanted ? 600 : 400) };
        // A tab without a name of its own is called after its first pane, so a pane rename or close can change it.
        var label = ShortTitle(TabLabel(host.Saved));
        if (host.Title.Text != label) host.Title.Text = label;
        RefreshPinMarks(host);
        // "Your turn" rather than "Finished" where the agent did not say how its turn ended: Claude Code's title reads the same at
        // a permission prompt as after a finished turn. Where it did say (OSC 7501), the word is its own: Needs you, Done or Failed.
        var state = presence switch
        {
            PaneState.Blocked when running => BlockedKind(host) is { } kind ? $"Needs you ({kind})" : "Needs you",
            PaneState.Attention when running => "Your turn",
            PaneState.Done when running => "Done",
            PaneState.Error => "Failed",
            PaneState.Working => "Working…",
            _ => running ? "Running" : failed ? "Error" : exited ? "Exited" : starting ? "Starting" : "Not running"
        };
        ToolTipService.SetToolTip(host.Item, DisplayTitle(host.Saved) + "\n" + host.Panes[0].Descriptor.WorkingDirectory + "\n" + state);
        RefreshWorkspaceStates();
    }

    /// <summary>Re-reads each pane's tracker, rings the ready chime for a turn that ended, and repaints the tab and the status bar when anything changed.</summary>
    private void RefreshPresence(TabHost host)
    {
        var now = DateTimeOffset.UtcNow;
        var before = host.Panes.Select(pane => pane.PresenceState).ToArray();
        var after = host.Panes.Select(pane => pane.RefreshPresence(now)).ToArray();
        string? endedIn = null;
        var ended = false;
        foreach (var pane in host.Panes) if (pane.TakeEndedTurn()) { ended = true; endedIn ??= pane.Descriptor.WorkingDirectory; }
        if (ended) RingReadyChime(endedIn);
        // An agent that writes its usage to its own log or database has done so by the time its turn ends: the pane's model and
        // context window are read again then, rather than at the timer's next tick. Claude Code's comes by its own file, and
        // Codex's rollout is watched too (WatchCodexRollouts); this is the fallback for a day folder the watcher had not found.
        for (var index = 0; index < host.Panes.Count; index++)
            if (before[index] == PaneState.Working && after[index] != PaneState.Working && host.Panes[index].Descriptor is { AgentId: "codex" or "opencode" or "hermes", SessionId: { } session })
                _ = RefreshContextAsync(session);
        // The tab shows its most urgent pane (PaneState rises in urgency).
        var presence = after.Length == 0 ? PaneState.Idle : after.Max();
        if (presence == host.Presence && before.SequenceEqual(after)) return;
        host.Presence = presence;
        RefreshTabStatus(host);
        UpdateStatus();
    }

    // Whether the window is the active one, from its Activated events; in view also needs it shown and not minimized.
    private bool _windowActive;
    // The smoke cannot count on its window being in the foreground of a PC in use, so it says itself whether it is.
    private bool _smokeWindowActive = true;

    /// <summary>
    /// Whether the user can be watching the window: it is active, shown and not minimized. A pane is in view when its tab is
    /// the selected one in such a window; a turn that ends there is being watched, so it neither asks for attention nor rings
    /// the chime (<see cref="PresenceTracker.Selected"/>).
    /// </summary>
    internal bool WindowInView => (_options.SmokeTest ? _smokeWindowActive : _windowActive) && AppWindow.IsVisible
        && AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };

    /// <summary>For the smoke: makes the window count as active or not, whatever Windows says.</summary>
    internal void SetSmokeWindowActive(bool active) { _smokeWindowActive = active; RefreshInView(); }

    private void WatchWindowInView()
    {
        Activated += (_, args) => { _windowActive = args.WindowActivationState != WindowActivationState.Deactivated; RefreshInView(); };
        AppWindow.Changed += (_, args) => { if (args.DidVisibilityChange || args.DidPresenterChange) RefreshInView(); };
    }

    /// <summary>Tells every pane whether it is in view (its tab selected in a window in view), and repaints what changed.</summary>
    private void RefreshInView()
    {
        var host = SelectedHost;
        var inView = WindowInView;
        var now = DateTimeOffset.UtcNow;
        foreach (var open in _open.Values)
        {
            foreach (var item in open.Panes) item.Presence.Selected(inView && ReferenceEquals(open, host), now);
            RefreshPresence(open);
            // A Needs you dot holds still in view and blinks out of it, which a change of view alone decides.
            if (open.Presence is PaneState.Blocked or PaneState.Error) RefreshBlink();
        }
        ResumeLimits();
    }

    private DateTimeOffset? _lastChime;

    /// <summary>Chimes the window rang, or would have rung in the smoke, which counts them instead of sounding them.</summary>
    internal int ChimesRung { get; private set; }

    /// <summary>The id of the sound the last chime rang, or would have rung in the smoke.</summary>
    internal string? LastSoundRung { get; private set; }

    /// <summary>
    /// Rings the ready chime for a turn that ended out of view (Settings → Chime when an agent is ready): in another tab, or with
    /// the window behind another, minimized or in the tray; once for turns that end together (<see cref="ReadyChime.MayRing"/>). The sound is the
    /// one for the folder the turn ended in (<see cref="SoundFor"/>): a pinned folder's own, or the one chosen in Settings.
    /// </summary>
    private void RingReadyChime(string? workingDirectory = null)
    {
        if (!_preferences.ReadyChime) return;
        var now = DateTimeOffset.UtcNow;
        if (!ReadyChime.MayRing(_lastChime, now)) return;
        _lastChime = now;
        var sound = SoundFor(workingDirectory);
        ChimesRung++;
        LastSoundRung = sound.Id;
        if (!_options.SmokeTest) ChimeSound.Play(sound);
    }
}
