using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sic.Core.Appearance;
using Sic.Core.Limits;

namespace Sic.App;

/// <summary>
/// The plan limits of Claude and ChatGPT in the status bar (docs/decisions/usage-limits.md): one group per plan while a pane
/// that draws on it runs, behind the marks of those panes' agents and the plan's name, then each window as a label, a 30 px
/// meter with a tick at the share of the window's time passed, and the share used. Only the meter is coloured: on pace, ahead
/// of pace, near the limit (UsageLimits.State), in the theme's own hues lifted to 3:1 on the bar (Theme.Limit*). Claude Code
/// draws on the Claude plan, and so does a Hermes pane whose session is on Claude with Claude Code's login (HermesUsage); Codex
/// on the ChatGPT plan, and so do Hermes and OpenCode when set to it (ChatGptPlan). The readings are the agents' own: Claude
/// Code's status line, which SIC is for the panes it starts (ClaudeStatusLine), Codex's session logs, and Hermes's own
/// <c>hermes usage</c> for either plan.
/// </summary>
internal sealed partial class MainWindow
{
    private const string ClaudePlan = "claude", ChatGptPlanId = "chatgpt";
    /// <summary>The agents that can draw on a plan's limits; a pane of another agent never starts the timer.</summary>
    private static readonly string[] LimitAgents = ["claude", "codex", "hermes", "opencode"];
    private static readonly Dictionary<string, string> LimitNames = new() { [ClaudePlan] = "Claude", [ChatGptPlanId] = "ChatGPT" };
    /// <summary>The meter's width; the fill and the tick are placed on it.</summary>
    private const double MeterWidth = 30;
    /// <summary>How often the readings are read again and the meters redrawn: the time passed moves the tick, and Codex says nothing when it writes.</summary>
    private static readonly TimeSpan LimitsInterval = TimeSpan.FromSeconds(20);
    /// <summary>
    /// How often Hermes is asked for a plan's windows while a Hermes pane on that plan runs: each ask is a request of Hermes's.
    /// For the Claude plan, only when no reading of the status line's is younger than this, since Claude Code's come with each reply.
    /// </summary>
    private static readonly TimeSpan HermesUsageInterval = TimeSpan.FromMinutes(5);
    /// <summary>Below this width the status bar drops the plans' names and the history line, so the meters and numbers stay.</summary>
    private const double CompactStatusWidth = 900;

    /// <summary>The kept readings by plan; the smoke sets its own.</summary>
    internal readonly Dictionary<string, LimitReading> _limitReadings = new(StringComparer.Ordinal);
    /// <summary>When this run began: a reading made before it is drawn grey, its number dimmed, until the plan is read again.</summary>
    internal readonly DateTimeOffset _limitsSince = DateTimeOffset.UtcNow;
    private StackPanel _limitsPanel = null!;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _limitsTimer;
    private FileSystemWatcher? _limitsWatcher;
    private bool _limitsReading;
    private string _limitPanesShown = "";
    private bool _statusCompact;
    /// <summary>The provider of each Hermes session a pane runs, from its row in Hermes's database else the config's, as last read.</summary>
    private readonly Dictionary<string, string?> _hermesProviders = new(StringComparer.Ordinal);
    /// <summary>The provider Hermes's config names, as last read: what a Hermes pane draws on before its session is known.</summary>
    private string? _hermesConfigured;
    /// <summary>Whether the ChatGPT plan has been seen on this PC: a kept reading, Hermes set to it, or a Codex log carrying it.</summary>
    private bool _chatGptSeen;
    /// <summary>
    /// Whether the Claude plan has been seen: a reading of it is kept, from Claude Code's status line or from Hermes with Claude
    /// Code's login. A Hermes pane on Claude joins the plan's group only then: with an API key Hermes reports no windows.
    /// </summary>
    private bool ClaudeSeen => _limitReadings.ContainsKey(ClaudePlan);
    /// <summary>The provider of each OpenCode session a pane runs, as last read from OpenCode's database.</summary>
    private readonly Dictionary<string, string?> _openCodeProviders = new(StringComparer.Ordinal);
    /// <summary>When Hermes was last asked for each plan's windows, by its provider.</summary>
    private readonly Dictionary<string, DateTimeOffset> _hermesUsageAsked = new(StringComparer.Ordinal);

    /// <summary>The provider a Hermes pane draws on: its session's, as last read, else the config's.</summary>
    private string? HermesProviderOf(string? sessionId) => (sessionId is null ? null : _hermesProviders.GetValueOrDefault(sessionId)) ?? _hermesConfigured;

    /// <summary>
    /// The plans in use and the agents drawing on each, in the order shown (Claude, then ChatGPT; Codex, Hermes, OpenCode within
    /// it), from the running panes and what the last refresh learned of Hermes's and OpenCode's providers. None while the setting is off.
    /// </summary>
    internal IReadOnlyList<(string Plan, IReadOnlyList<string> Agents)> LimitPlansRunning()
    {
        if (!_preferences.ShowUsageLimits) return [];
        var running = AllPanes.Where(pane => pane.IsRunning).Select(pane => pane.Descriptor).ToList();
        bool Runs(string agent) => running.Any(pane => pane.AgentId == agent);
        var plans = new List<(string, IReadOnlyList<string>)>();
        bool HermesOn(string provider) => running.Any(pane => pane.AgentId == "hermes" && HermesProviderOf(pane.SessionId) == provider);
        var claude = new List<string>();
        if (Runs("claude")) claude.Add("claude");
        if (ClaudeSeen && HermesOn(HermesUsage.ClaudeProvider)) claude.Add("hermes");
        if (claude.Count > 0) plans.Add((ClaudePlan, claude));
        var chatGpt = new List<string>();
        if (Runs("codex")) chatGpt.Add("codex");
        if (HermesOn(HermesUsage.ChatGptProvider)) chatGpt.Add("hermes");
        if (_chatGptSeen && running.Any(pane => pane.AgentId == "opencode" && pane.SessionId is { } id
            && _openCodeProviders.GetValueOrDefault(id) == ChatGptPlan.OpenCodeProvider)) chatGpt.Add("opencode");
        if (chatGpt.Count > 0) plans.Add((ChatGptPlanId, chatGpt));
        return plans;
    }

    /// <summary>The running panes of the agents that can draw on a plan, with their sessions: what decides whether the groups need another look.</summary>
    private string LimitPanesKey() => !_preferences.ShowUsageLimits ? "" : string.Join(",", AllPanes
        .Where(pane => pane.IsRunning && LimitAgents.Contains(pane.Descriptor.AgentId))
        .Select(pane => pane.Descriptor.AgentId + ":" + pane.Descriptor.SessionId).Order(StringComparer.Ordinal));

    /// <summary>The groups' panel, at the bar's right end.</summary>
    private StackPanel BuildLimitsPanel() => _limitsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// Called with every status update: when the running panes that can draw on a plan change, the groups are redrawn at once
    /// from what is known and the readings are read again; the timer and the watchers run while any runs, and rest, with the
    /// watchers disposed, while none does.
    /// </summary>
    private void FollowLimits()
    {
        var key = LimitPanesKey();
        if (key == _limitPanesShown) return;
        _limitPanesShown = key;
        RenderLimits();
        // The context meters follow the same changes, at once rather than after the sweep (which may be waiting on Hermes): a
        // pane that started reads its session's reading, one that exited loses its meter, and the setting turned off takes them all away.
        _ = RefreshContextAsync();
        if (key.Length == 0)
        {
            _limitsTimer?.Stop();
            _limitsPaused = false;
            StopWatching();
            return;
        }
        WatchReadings();
        if (_limitsTimer is null)
        {
            _limitsTimer = DispatcherQueue.CreateTimer();
            _limitsTimer.Interval = LimitsInterval;
            // Nothing reads the bar in the tray or minimized: the tick waits, and the window's return runs the sweep it skipped (ResumeLimits).
            _limitsTimer.Tick += async (_, _) => { if (!WindowShown) { _limitsPaused = true; return; } await RefreshLimitsAsync(); };
        }
        if (!_limitsTimer.IsRunning) _limitsTimer.Start();
        // The meters were read just now; the sweep reads the plans alone.
        _ = RefreshLimitsAsync(context: false);
    }

    /// <summary>Whether the window is on screen at all: shown, and not minimized. A tab or another window in front still counts.</summary>
    private bool WindowShown => AppWindow.IsVisible && AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
    /// <summary>Set by a tick that found the window hidden; the next time it is in view, the sweep runs at once.</summary>
    private bool _limitsPaused;

    /// <summary>The sweep a hidden window's ticks skipped, run as the window comes back (RefreshInView).</summary>
    private void ResumeLimits()
    {
        if (!_limitsPaused || !WindowShown || _limitsTimer is not { IsRunning: true }) return;
        _limitsPaused = false;
        _ = RefreshLimitsAsync();
    }

    /// <summary>Whether a pane runs that draws on the ChatGPT plan, or may: Codex, Hermes (on that plan or not, which the sweep reads), OpenCode.</summary>
    private bool ChatGptWanted() => AllPanes.Any(pane => pane.IsRunning && pane.Descriptor.AgentId is "codex" or "hermes" or "opencode");

    /// <summary>
    /// The watchers that bring a reading as it is written: Claude's status line writes the plan's reading and the session's
    /// context after each reply (and with each message update while Claude works, as often as every 300 ms), and Codex appends
    /// to its rollout as a response streams. Each watcher reads only what was written and redraws only what changed; the sweep
    /// of every plan and every pane stays with the timer and with changes to the panes drawing on a plan (FollowLimits).
    /// </summary>
    private void WatchReadings()
    {
        if (_closing) return;
        if (_limitsWatcher is null)
        {
            try
            {
                var folder = ClaudeStatusLine.LimitsFolder(_options.DataDirectory);
                Directory.CreateDirectory(folder);
                _limitsWatcher = new FileSystemWatcher(folder, ClaudeStatusLine.ReadingFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
                void Changed(object? sender, FileSystemEventArgs args) => DispatcherQueue.TryEnqueue(async () => await RefreshClaudeReadingAsync());
                _limitsWatcher.Changed += Changed; _limitsWatcher.Created += Changed; _limitsWatcher.Renamed += (sender, args) => Changed(sender, args);
                _limitsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Diagnostics.Log("usage limits: the limits folder cannot be watched: " + exception.Message);
            }
        }
        WatchContextReadings();
        WatchCodexRollouts();
    }

    /// <summary>Disposes the watchers while no pane draws on a plan; <see cref="WatchReadings"/> makes them again.</summary>
    private void StopWatching()
    {
        _limitsWatcher?.Dispose(); _limitsWatcher = null;
        _contextWatcher?.Dispose(); _contextWatcher = null;
        _codexWatcher?.Dispose(); _codexWatcher = null; _codexWatchedFolder = null;
        _codexWatchDebounce?.Stop();
    }

    /// <summary>
    /// Claude's kept reading alone, as its status line wrote it: kept when newer, drawn only when its windows changed, since the
    /// line runs with every message update and most of its writes repeat the windows with a later time. Nothing else is read.
    /// </summary>
    private async Task RefreshClaudeReadingAsync()
    {
        if (_closing || !_preferences.ShowUsageLimits) return;
        var data = _options.DataDirectory;
        var reading = await Task.Run(() => ClaudeStatusLine.Read(data));
        if (_closing || reading is null) return;
        var kept = _limitReadings.GetValueOrDefault(ClaudePlan);
        var latest = UsageLimits.Latest(kept, reading)!;
        _limitReadings[ClaudePlan] = latest;
        // The first reading also lets a Hermes pane on Claude join the group.
        if (kept is null || !SameBar(kept, latest)) RenderLimits();
    }

    /// <summary>
    /// Whether two readings draw the same bar: the same windows, and both from this run or both from before it. The time read is
    /// otherwise left out, since it alone changes nothing on the bar.
    /// </summary>
    private bool SameBar(LimitReading? a, LimitReading? b) =>
        a?.Short == b?.Short && a?.Long == b?.Long && (a?.ReadAt < _limitsSince) == (b?.ReadAt < _limitsSince);

    private FileSystemWatcher? _codexWatcher;
    private string? _codexWatchedFolder;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _codexWatchDebounce;
    private bool _codexReading;
    /// <summary>How long after Codex's last write to a rollout the log is read: a response streams in many writes, and one read at the end of a burst is enough.</summary>
    private static readonly TimeSpan CodexWriteSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Watches the folder Codex writes the day's rollouts to, while a pane that draws on the ChatGPT plan runs, so a plan reading
    /// or a token count it appends reaches the bar and the pane's context meter within a second rather than at the timer's
    /// next tick: no polling, one tail read per burst of writes. The day's folder appears with the day's first session and
    /// changes at midnight, so each timer tick looks again (<see cref="RefreshLimitsAsync"/>); the watcher is re-pointed when
    /// the folder differs, and disposed while no such pane runs, since a Codex run outside SicTerm would otherwise read for a bar that shows no ChatGPT group.
    /// </summary>
    private void WatchCodexRollouts()
    {
        if (_closing) return;
        var folder = ChatGptWanted() && _preferences.Agents.FirstOrDefault(agent => agent.Id == "codex") is { } codex ? UsageLimits.CodexDayFolder(codex.HistoryDirectory, DateTime.Now) : null;
        if (_codexWatcher is not null && string.Equals(folder, _codexWatchedFolder, StringComparison.OrdinalIgnoreCase)) return;
        _codexWatcher?.Dispose(); _codexWatcher = null; _codexWatchedFolder = null;
        if (folder is null || !Directory.Exists(folder)) return;
        try
        {
            _codexWatcher = new FileSystemWatcher(folder, "rollout-*.jsonl") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            void Changed(object? sender, FileSystemEventArgs args) => DispatcherQueue.TryEnqueue(RefreshCodexAfterWrites);
            _codexWatcher.Changed += Changed; _codexWatcher.Created += Changed; _codexWatcher.Renamed += (sender, args) => Changed(sender, args);
            _codexWatcher.EnableRaisingEvents = true;
            _codexWatchedFolder = folder;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _codexWatcher?.Dispose(); _codexWatcher = null;
            Diagnostics.Log("usage limits: Codex's rollout folder cannot be watched: " + exception.Message);
        }
    }

    /// <summary>One Codex reading once its writes have settled; a write during the wait starts it over.</summary>
    private void RefreshCodexAfterWrites()
    {
        if (_closing) return;
        if (_codexWatchDebounce is null)
        {
            _codexWatchDebounce = DispatcherQueue.CreateTimer();
            _codexWatchDebounce.Interval = CodexWriteSettle;
            _codexWatchDebounce.IsRepeating = false;
            _codexWatchDebounce.Tick += async (_, _) => await RefreshCodexAsync();
        }
        _codexWatchDebounce.Stop();
        _codexWatchDebounce.Start();
    }

    /// <summary>
    /// Codex wrote to a rollout: the ChatGPT plan's windows from the Codex logs alone, kept and drawn when they changed, then the
    /// Codex panes' model and context from the same logs. The other plans and panes keep to the timer.
    /// </summary>
    private async Task RefreshCodexAsync()
    {
        if (_closing || _codexReading || !_preferences.ShowUsageLimits || !ChatGptWanted() || _preferences.Agents.FirstOrDefault(agent => agent.Id == "codex") is not { } codexAgent) return;
        _codexReading = true;
        try
        {
            var sessions = CodexSessionKeys();
            var data = _options.DataDirectory;
            var reading = await Task.Run(() =>
            {
                var codex = CodexPlanReading(codexAgent, sessions);
                // A newer reading than the kept one is kept, so the plan counts as seen across restarts.
                if (codex is not null && !(ChatGptPlan.Read(data) is { } stored && stored.ReadAt >= codex.ReadAt)) ChatGptPlan.Write(data, codex);
                return codex;
            });
            if (!_closing && reading is not null)
            {
                var kept = _limitReadings.GetValueOrDefault(ChatGptPlanId);
                var latest = UsageLimits.Latest(kept, reading)!;
                _limitReadings[ChatGptPlanId] = latest;
                _chatGptSeen = true;
                if (!SameBar(kept, latest)) RenderLimits();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Diagnostics.Log("usage limits: a Codex reading could not be read: " + exception.Message);
        }
        finally { _codexReading = false; }
        // The Codex panes' model and context come from the same logs; a write with no plan reading still moves them.
        if (!_closing) await RefreshContextAsync(agentId: "codex");
    }

    /// <summary>The running Codex panes' sessions, as the catalog keys them.</summary>
    private string[] CodexSessionKeys() => AllPanes.Where(pane => pane.IsRunning && pane.Descriptor is { AgentId: "codex", SessionId: not null })
        .Select(pane => "codex:" + pane.Descriptor.SessionId).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The latest plan windows in the running Codex panes' logs and the newest logs of the last two days, or null. Off the UI thread.</summary>
    private LimitReading? CodexPlanReading(Sic.Core.Agents.AgentDefinition codexAgent, IEnumerable<string> sessionKeys)
    {
        LimitReading? codex = null;
        var logs = sessionKeys.Select(key => _catalog.GetHeader(key)?.SourcePath).OfType<string>()
            .Concat(UsageLimits.RecentCodexRollouts(codexAgent.HistoryDirectory, DateTime.Now)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var log in logs) codex = UsageLimits.Latest(codex, UsageLimits.FromCodexRollout(log));
        return codex;
    }

    private FileSystemWatcher? _contextWatcher;
    private bool _contextPruned;
    /// <summary>How long a session's kept context reading outlives its last update; the files are cleared of older ones once per run.</summary>
    private static readonly TimeSpan ContextReadingsKept = TimeSpan.FromDays(30);

    /// <summary>
    /// The status line writes each Claude Code session's context window to a file of its own (<see cref="ClaudeStatusLine.ContextFile"/>)
    /// after each reply; the watcher brings it to that session's pane at once (docs/decisions/context-window.md).
    /// </summary>
    private void WatchContextReadings()
    {
        if (_contextWatcher is not null || _closing) return;
        try
        {
            var folder = ClaudeStatusLine.ContextFolder(_options.DataDirectory);
            Directory.CreateDirectory(folder);
            _contextWatcher = new FileSystemWatcher(folder, "*.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            void Changed(object? sender, FileSystemEventArgs args)
            {
                // The helper writes a temporary file and moves it into place; only the file named for a session is a reading.
                if (ClaudeStatusLine.ContextSessionOf(args.FullPath) is { } session) DispatcherQueue.TryEnqueue(async () => await ContextWrittenAsync(session));
            }
            _contextWatcher.Changed += Changed; _contextWatcher.Created += Changed; _contextWatcher.Renamed += (sender, args) => Changed(sender, args);
            _contextWatcher.EnableRaisingEvents = true;
            // Once per run, though the watcher is made again each time a pane that draws on a plan starts after none ran.
            if (!_contextPruned) { _contextPruned = true; _ = Task.Run(() => ClaudeStatusLine.PruneContext(_options.DataDirectory, ContextReadingsKept, DateTimeOffset.UtcNow)); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Diagnostics.Log("usage limits: the context folder cannot be watched: " + exception.Message);
        }
    }

    /// <summary>
    /// A Claude Code session's reading was written. When it came from a running Claude Code pane's own process (the pane's ID
    /// and the process are in it, <see cref="PaneOrigin"/>) and names another session than the pane's, that process has moved on,
    /// with /clear or an in-pane /resume, and the pane follows it (<see cref="FollowSession"/>); a reading of the pane's own
    /// session tells the pane which process is its own. Then the session's pane shows the reading.
    /// </summary>
    private async Task ContextWrittenAsync(string session)
    {
        if (_closing) return;
        var data = _options.DataDirectory;
        var reading = await Task.Run(() => ClaudeStatusLine.ReadContext(data, session));
        if (_closing) return;
        if (_preferences.ShowUsageLimits && reading is { Pane: { } paneId, AgentProcess: { } process }
            && AllPanes.FirstOrDefault(pane => pane.Descriptor.Id == paneId && pane.Descriptor.AgentId == "claude" && pane.IsRunning) is { } pane)
        {
            if (string.Equals(pane.Descriptor.SessionId, reading.SessionId, StringComparison.OrdinalIgnoreCase)) pane.AgentProcess ??= process;
            else if (PaneOrigin.Moves(pane.Descriptor.SessionId, pane.AgentProcess, reading))
            {
                pane.AgentProcess = process;
                FollowSession(pane, reading.SessionId);
            }
        }
        await RefreshContextAsync(session);
    }

    /// <summary>
    /// Moves a running Claude Code pane to the session its process went on to (<see cref="ContextWrittenAsync"/>): the pane
    /// resumes, links and is found under that session from here on, and the session it left is a closed conversation like any
    /// other. The name and color the pane wore are copied to the new conversation unless it has its own (November 11, 2026, with
    /// Chris), with the pane's folder; it is unsaved until the catalog holds its transcript (<see cref="NoteSavedSessionsAsync"/>).
    /// </summary>
    private void FollowSession(SessionPane pane, string sessionId)
    {
        if (HostOf(pane) is not { } host) return;
        var was = pane.Descriptor;
        var key = TabLabels.Key(was.AgentId, sessionId);
        var header = _catalog.GetHeader(key);
        var name = (pane.ConversationKey is { } old ? _preferences.ConversationNames.GetValueOrDefault(old) : null)
            ?? was.CustomTitle ?? (host.Panes.Count == 1 ? host.Saved.CustomTitle : null);
        if (name is not null) _preferences.ConversationNames.TryAdd(key, name);
        if (host.Saved.Color is { } color) _preferences.ConversationColors.TryAdd(key, color);
        if (Path.IsPathFullyQualified(was.WorkingDirectory)) _preferences.ConversationFolders.TryAdd(key, was.WorkingDirectory);
        pane.UpdateIdentity(was with { SessionId = sessionId, Title = header?.Title ?? NewTitle(was.AgentId, was.WorkingDirectory), CreationName = null, Unsaved = header is null });
        host.Saved = host.Saved with { Panes = host.Panes.Select(item => item.Descriptor).ToList() };
        host.Item.Tag = host.Saved;
        Diagnostics.Log("context window: pane " + was.Id + " followed its Claude Code process from " + (was.SessionId ?? "no session") + " to " + sessionId);
        RefreshTabStatus(host);
        PaintTab(host);
        SavePreferences();
        SaveLayout();
        RefreshConversationRows();
    }

    /// <summary>
    /// Reads the model and context window of <paramref name="sessionId"/>'s pane, of <paramref name="agentId"/>'s panes, or of
    /// every agent pane, off the UI thread and shows it in the pane's header: while the pane runs and the setting is on, else
    /// the meter goes. Each agent's reading comes from what it writes (docs/decisions/context-window.md): Claude Code's from
    /// the status line's kept file, Codex's from its rollout, OpenCode's and Hermes's from their databases.
    /// </summary>
    internal async Task RefreshContextAsync(string? sessionId = null, string? agentId = null)
    {
        if (_closing) return;
        foreach (var pane in AllPanes.Where(pane => LimitAgents.Contains(pane.Descriptor.AgentId) && (agentId is null || pane.Descriptor.AgentId == agentId)
            && (sessionId is null || string.Equals(pane.Descriptor.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            if (!_preferences.ShowUsageLimits || !pane.IsRunning || pane.Descriptor.SessionId is not { } id) { pane.ShowContext(null); continue; }
            var paneAgent = pane.Descriptor.AgentId;
            var definition = _preferences.Agents.FirstOrDefault(agent => agent.Id == paneAgent);
            var data = _options.DataDirectory;
            var reading = await Task.Run(() => ReadContext(paneAgent, id, definition, data));
            if (_closing) return;
            // The pane may have exited, or its session changed, while the reading was made.
            if (pane.IsRunning && string.Equals(pane.Descriptor.SessionId, id, StringComparison.OrdinalIgnoreCase)) pane.ShowContext(reading);
        }
    }

    /// <summary>One session's reading from its agent's own files; null when the agent has written nothing for it yet.</summary>
    private ContextReading? ReadContext(string agentId, string sessionId, Sic.Core.Agents.AgentDefinition? definition, string data)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            switch (agentId)
            {
                case "claude": return ClaudeStatusLine.ReadContext(data, sessionId);
                case "codex":
                    // The rollout the catalog recorded for the session; a new session's is known once its first message has bound it.
                    return _catalog.GetHeader(Sic.Core.Appearance.TabLabels.Key(agentId, sessionId))?.SourcePath is { } rollout && File.Exists(rollout) ? CodexContext.Read(sessionId, rollout, now) : null;
                case "opencode": return definition is null ? null : OpenCodeContext.Read(definition, sessionId, now);
                case "hermes": return definition is null ? null : HermesContext.Read(definition, sessionId, now);
                default: return null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        {
            Diagnostics.Log("context window: " + agentId + " " + sessionId + ": " + exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads off the UI thread what decides the groups and their numbers, keeps each plan's latest reading, and redraws: the
    /// Hermes panes' sessions' providers (their rows, else the config); Claude's kept reading while a Claude Code pane runs or a
    /// Hermes pane is on Claude, and Hermes's own figures for the Claude plan when no reading of the status line's is younger
    /// than <see cref="HermesUsageInterval"/>; and while a pane that draws on the ChatGPT plan runs (Codex, Hermes, OpenCode),
    /// OpenCode's sessions' providers, the plan's kept reading, the running Codex panes' logs and the newest logs of the last
    /// two days, and Hermes's own figures, asked for at most every <see cref="HermesUsageInterval"/> while a Hermes pane on
    /// the plan runs. A newer reading of either plan from Hermes is kept in the data folder. A pane of one plan alone reads
    /// nothing of the other's: a Claude Code session used to read the Codex logs every 20 seconds for a group not shown.
    /// With <paramref name="context"/>, the panes' models and context windows are read again afterwards, as the timer has them.
    /// </summary>
    internal async Task RefreshLimitsAsync(bool context = true)
    {
        if (_limitsReading || _closing) return;
        _limitsReading = true;
        // The day's rollout folder may have appeared, or the day changed, since the watcher was pointed.
        if (_limitsWatcher is not null) WatchCodexRollouts();
        try
        {
            var panes = AllPanes.Where(pane => pane.IsRunning).Select(pane => pane.Descriptor).ToList();
            if (_preferences.ShowUsageLimits && panes.Any(pane => LimitAgents.Contains(pane.AgentId)))
            {
                var data = _options.DataDirectory;
                var agents = _preferences.Agents.ToDictionary(agent => agent.Id, StringComparer.Ordinal);
                var hermesExecutable = _detection.GetValueOrDefault("hermes")?.Path ?? agents.GetValueOrDefault("hermes")?.Executable;
                var openCodeSessions = panes.Where(pane => pane.AgentId == "opencode" && pane.SessionId is not null).Select(pane => pane.SessionId!).Distinct().ToList();
                var codexSessions = CodexSessionKeys();
                var hermesPanes = panes.Where(pane => pane.AgentId == "hermes").Select(pane => pane.SessionId).ToList();
                var hermesSessions = hermesPanes.OfType<string>().Distinct().ToList();
                bool Runs(string agent) => panes.Any(pane => pane.AgentId == agent);
                var hermesRuns = hermesPanes.Count > 0;
                var chatGptWanted = Runs("codex") || hermesRuns || Runs("opencode");
                var now = DateTimeOffset.UtcNow;
                bool AskHermes(string provider) => now - _hermesUsageAsked.GetValueOrDefault(provider, DateTimeOffset.MinValue) >= HermesUsageInterval;
                var (askChatGpt, askClaude) = (AskHermes(HermesUsage.ChatGptProvider), AskHermes(HermesUsage.ClaudeProvider));
                var known = new Dictionary<string, string?>(_openCodeProviders, StringComparer.Ordinal);
                var (claudeKept, seenBefore, configuredBefore) = (_limitReadings.GetValueOrDefault(ClaudePlan), _chatGptSeen, _hermesConfigured);
                var result = await Task.Run(() =>
                {
                    var hermesAgent = agents.GetValueOrDefault("hermes");
                    // Each Hermes pane's provider: its session's row, else the config's; what the pane draws on.
                    var configured = hermesRuns && hermesAgent is not null ? ChatGptPlan.HermesConfiguredProvider(hermesAgent.HistoryDirectory) : configuredBefore;
                    var hermesProviders = new Dictionary<string, string?>(StringComparer.Ordinal);
                    if (hermesRuns && hermesAgent is not null)
                        foreach (var id in hermesSessions) hermesProviders[id] = Sic.Core.Limits.HermesContext.Provider(hermesAgent, id) ?? configured;
                    var drawnOn = hermesPanes.Select(id => (id is null ? null : hermesProviders.GetValueOrDefault(id)) ?? configured).ToList();
                    var (hermesOnClaude, hermesOnChatGpt) = (drawnOn.Contains(HermesUsage.ClaudeProvider), drawnOn.Contains(HermesUsage.ChatGptProvider));
                    // The Claude plan: the status line's file, and Hermes's own figures when no reading is younger than the
                    // interval, since a Claude Code pane's come with each reply and the plan is the same account's.
                    var claude = Runs("claude") || hermesOnClaude ? ClaudeStatusLine.Read(data) : null;
                    var askedClaude = false;
                    if (hermesOnClaude && askClaude && hermesExecutable is not null && hermesAgent is not null
                        && !((UsageLimits.Latest(claudeKept, claude)?.ReadAt ?? DateTimeOffset.MinValue) > now - HermesUsageInterval))
                    {
                        askedClaude = true;
                        if (HermesUsage.Run(hermesAgent, hermesExecutable, HermesUsage.ClaudeProvider) is { } fromHermes && (claude is null || fromHermes.ReadAt > claude.ReadAt))
                        {
                            claude = fromHermes;
                            ClaudeStatusLine.Write(data, fromHermes);
                        }
                    }
                    // No pane draws on the ChatGPT plan: nothing of it is read, and what was known of it stays.
                    if (!chatGptWanted) return (claude, configured, hermesProviders, providers: new Dictionary<string, string?>(StringComparer.Ordinal), chatGpt: (LimitReading?)null, seen: seenBefore, askedChatGpt: false, askedClaude);
                    var providers = new Dictionary<string, string?>(StringComparer.Ordinal);
                    if (agents.GetValueOrDefault("opencode") is { } openCode)
                        foreach (var id in openCodeSessions)
                            // A provider once known stays; one not yet written (before the session's first message) is looked for again.
                            providers[id] = known.GetValueOrDefault(id) ?? Sic.Core.Sessions.OpenCodeHistoryReader.Provider(openCode, id);
                    var kept = ChatGptPlan.Read(data);
                    var codex = agents.GetValueOrDefault("codex") is { } codexAgent ? CodexPlanReading(codexAgent, codexSessions) : null;
                    var askedChatGpt = hermesOnChatGpt && askChatGpt && hermesExecutable is not null && hermesAgent is not null;
                    var fromHermesChatGpt = askedChatGpt ? HermesUsage.Run(hermesAgent!, hermesExecutable!, HermesUsage.ChatGptProvider) : null;
                    var chatGpt = UsageLimits.Latest(UsageLimits.Latest(kept, codex), fromHermesChatGpt);
                    if (chatGpt is not null && chatGpt != kept) ChatGptPlan.Write(data, chatGpt);
                    return (claude, configured, hermesProviders, providers, chatGpt, seen: chatGpt is not null || hermesOnChatGpt, askedChatGpt, askedClaude);
                });
                if (result.claude is not null) _limitReadings[ClaudePlan] = UsageLimits.Latest(_limitReadings.GetValueOrDefault(ClaudePlan), result.claude)!;
                if (result.chatGpt is not null) _limitReadings[ChatGptPlanId] = UsageLimits.Latest(_limitReadings.GetValueOrDefault(ChatGptPlanId), result.chatGpt)!;
                _hermesConfigured = result.configured;
                _hermesProviders.Clear();
                foreach (var (id, provider) in result.hermesProviders) _hermesProviders[id] = provider;
                _chatGptSeen = result.seen;
                foreach (var (id, provider) in result.providers) _openCodeProviders[id] = provider;
                if (result.askedChatGpt) _hermesUsageAsked[HermesUsage.ChatGptProvider] = DateTimeOffset.UtcNow;
                if (result.askedClaude) _hermesUsageAsked[HermesUsage.ClaudeProvider] = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            Diagnostics.Log("usage limits: a reading could not be read: " + exception.Message);
        }
        finally { _limitsReading = false; }
        if (_closing) return;
        RenderLimits();
        // The agents that write their usage to their own logs and databases are read again with the plans, on the same timer.
        if (context) await RefreshContextAsync();
    }

    /// <summary>Redraws the groups from the kept readings at the present time; cheap, so it runs with each refresh.</summary>
    private void RenderLimits(IReadOnlyList<(string Plan, IReadOnlyList<string> Agents)>? plans = null)
    {
        _limitsPanel.Children.Clear();
        var now = DateTimeOffset.Now;
        foreach (var (plan, agents) in plans ?? LimitPlansRunning())
        {
            var divider = new Border { Width = 1, Height = 14, Background = _theme.LimitDivider, VerticalAlignment = VerticalAlignment.Center };
            _limitsPanel.Children.Add(divider);
            _limitsPanel.Children.Add(LimitGroup(plan, agents, _limitReadings.GetValueOrDefault(plan), now));
        }
    }

    /// <summary>
    /// One plan's group: the marks of the agents drawing on it (the tabs' own tiles, smaller), the plan's name, and a meter per
    /// window, or a dash before any reading.
    /// </summary>
    private StackPanel LimitGroup(string id, IReadOnlyList<string> agents, LimitReading? reading, DateTimeOffset now)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Tag = id, Background = _theme.Transparent };
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        foreach (var agent in agents) marks.Children.Add(_theme.AgentTile(agent, 14));
        group.Children.Add(marks);
        var name = BarText(LimitNames[id]);
        name.FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 };
        name.Visibility = _statusCompact ? Visibility.Collapsed : Visibility.Visible;
        group.Children.Add(name);
        var windows = reading?.Windows.ToList() ?? [];
        if (windows.Count == 0)
        {
            var dash = BarText("—"); dash.Opacity = 0.72;
            group.Children.Add(dash);
        }
        // A reading kept from before this run may be another account's, or the plan may have moved or been reset since.
        var earlier = reading is not null && reading.ReadAt < _limitsSince;
        foreach (var window in windows) group.Children.Add(LimitWindowView(window, now, earlier));
        ToolTipService.SetToolTip(group, LimitTooltip(reading, now, earlier));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(group, LimitNames[id] + " usage limits");
        return group;
    }

    /// <summary>One window's label, meter and number; <paramref name="earlier"/>, a reading from before this run, is drawn grey and dimmed, never bold.</summary>
    private StackPanel LimitWindowView(LimitWindow window, DateTimeOffset now, bool earlier = false)
    {
        var state = UsageLimits.State(window, now);
        var view = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        var label = BarText(window.Label); label.Opacity = 0.72;
        view.Children.Add(label);
        var meter = new Grid { Width = MeterWidth, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        meter.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = _theme.LimitTrack, VerticalAlignment = VerticalAlignment.Center });
        var used = state == LimitState.Unknown ? 0 : Math.Clamp(window.UsedPercent, 0, 100);
        if (used > 0)
            meter.Children.Add(new Border
            {
                Width = Math.Max(2, MeterWidth * used / 100), Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Background = earlier ? _theme.LimitUnknown : LimitBrush(state)
            });
        // The tick: how much of the window's time has passed. Fill past it means the limit is being used faster than time passes.
        if (state != LimitState.Unknown)
            meter.Children.Add(new Border
            {
                Width = 1, Height = 8, HorizontalAlignment = HorizontalAlignment.Left, Background = _theme.BarInk,
                Margin = new Thickness(Math.Clamp(MeterWidth * window.ElapsedPercent(now) / 100 - 0.5, 0, MeterWidth - 1), 0, 0, 0)
            });
        view.Children.Add(meter);
        var number = BarText(state switch
        {
            LimitState.Unknown => "—",
            LimitState.Reached => window.ResetsAt.ToLocalTime().ToString("t"),
            _ => Math.Round(window.UsedPercent) + "%"
        });
        number.MinWidth = 26; number.TextAlignment = TextAlignment.Right;
        if (earlier) number.Opacity = 0.72;
        else if (state is LimitState.Near or LimitState.Reached) number.FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 };
        view.Children.Add(number);
        return view;
    }

    private Brush LimitBrush(LimitState state) => state switch
    {
        LimitState.OnPace => _theme.LimitOnPace,
        LimitState.Ahead => _theme.LimitAhead,
        LimitState.Near or LimitState.Reached => _theme.LimitNear,
        _ => _theme.LimitUnknown
    };

    private TextBlock BarText(string text) => new()
    {
        Text = text, FontSize = 12, FontFamily = Theme.Body, Foreground = _theme.BarInk, VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// The group's words: only what the bar does not show, which is when each window resets ("5h: Resets @ 6:01pm"), or that
    /// there is no reading yet; for a reading from before this run (<paramref name="earlier"/>), when it was made, first.
    /// The plan, its agents and the share used are on the bar itself.
    /// </summary>
    internal static string LimitTooltip(LimitReading? reading, DateTimeOffset now, bool earlier = false)
        => reading is null || !reading.Windows.Any() ? "No reading yet"
            : (earlier ? "Last reading " + When(reading.ReadAt, now) + ", before SicTerm started" + Environment.NewLine : "")
              + string.Join(Environment.NewLine, reading.Windows.Select(window => window.Label + ": " + (window.ResetsAt <= now ? "Reset " : "Resets ") + When(window.ResetsAt, now)));

    /// <summary>A time today as @ 6:01pm, another day with its weekday: Sun @ 7:02am.</summary>
    private static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        // A twelve-hour clock reads 6:01pm, as compact as the bar it explains; a twenty-four-hour one is the culture's own.
        var clock = culture.DateTimeFormat.ShortTimePattern.Contains('t')
            ? local.ToString("h:mm", culture) + local.ToString("tt", culture).ToLowerInvariant() : local.ToString("t", culture);
        return (local.Date == now.ToLocalTime().Date ? "@ " : local.ToString("ddd", culture) + " @ ") + clock;
    }

    /// <summary>Drops the agents' names and the history line from a narrow status bar, so the meters and numbers keep their room.</summary>
    private void FitStatusBar(double width)
    {
        var compact = width < CompactStatusWidth;
        if (compact == _statusCompact) return;
        _statusCompact = compact;
        _statusDetail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RenderLimits();
    }

    /// <summary>
    /// The arguments that make SIC Claude Code's status line for a pane it starts: <c>--settings</c> and a file in the data folder
    /// that keeps the user's own status line's fields and names SicTermStatusLine.exe as its command. None when the setting is
    /// off, for another agent, when the agent's own arguments already give settings, when the helper is missing, or when the
    /// file cannot be written.
    /// </summary>
    private string[]? ClaudeLimitArguments(Sic.Core.Agents.AgentDefinition agent, string directory)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, ClaudeStatusLine.ExecutableName);
        if (agent.Id != "claude" || !_preferences.ShowUsageLimits || agent.Arguments.Contains("--settings") || !File.Exists(executable)) return null;
        try
        {
            var command = ClaudeStatusLine.Command(executable, _options.Isolated ? _options.DataDirectory : null, ShortPath);
            var settings = ClaudeStatusLine.SettingsJson(command, ClaudeStatusLine.UserStatusLine(agent.HistoryDirectory, directory));
            return ["--settings", ClaudeStatusLine.WriteSettings(_options.DataDirectory, settings)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log("usage limits: Claude Code's settings could not be written: " + exception.Message);
            return null;
        }
    }

    /// <summary>The 8.3 form of a path with spaces, which both shells take unquoted; the path itself when Windows has none.</summary>
    private static string ShortPath(string path)
    {
        var buffer = new char[512];
        var length = GetShortPathName(path, buffer, buffer.Length);
        return length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : path;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, int length);

    /// <summary>Settings → Agents → Show usage limits: the groups come or go at once; Claude Code panes already running keep their status line.</summary>
    internal void SetShowUsageLimits(bool on)
    {
        _preferences.ShowUsageLimits = on;
        SavePreferences();
        _limitPanesShown = "-";
        FollowLimits();
    }

    /// <summary>Stops reading limits as SIC closes.</summary>
    private void StopLimits()
    {
        _limitsTimer?.Stop();
        StopWatching();
    }
}
