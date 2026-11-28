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

/// <summary>Opening and starting sessions: a saved conversation as a placeholder, its resume, a new session, and the binding of a new Codex or Hermes session to its identity.</summary>
internal sealed partial class MainWindow
{
    // New Hermes sessions under way, one at a time per folder until each is linked (BindCreatedHermesAsync).
    private readonly CreationTurns _hermesTurns = new();

    /// <summary>
    /// Opens a saved conversation as an inactive placeholder: as a new tab, or, asked for a split, beside or below the selected
    /// tab's focused pane, along its longer side, or the other way when only that fits. A tab with no room gives it a tab of its own.
    /// </summary>
    private Task<SessionPane?> OpenConversationAsync(Conversation conversation, bool split = false)
    {
        try
        {
            if (FindPane(conversation) is { } existing)
            {
                // In another workspace's tab too: the strip switches there first.
                ShowHost(HostOf(existing)!);
                SetActivePane(existing);
                return Task.FromResult<SessionPane?>(existing);
            }
            var pane = new SavedPane(Guid.NewGuid().ToString("N"), conversation.AgentId, conversation.Title,
                FolderOf(conversation) ?? "", conversation.HistoryDirectory, conversation.SessionId);
            SessionPane created;
            if (split && SelectedHost is { Closing: false } target && target.Panes.Count < PaneLayout.MaxPanes && FocusedPane(target) is { } source && SplitThatFits(source) is { } direction)
                created = AddPane(target, pane, source, direction);
            else
            {
                if (_tabs.TabItems.Count >= Workspaces.MaxTabs) throw new InvalidOperationException($"A workspace holds up to {Workspaces.MaxTabs} tabs.");
                var original = _catalog.GetHeader(conversation.Key)?.Title ?? conversation.Title;
                var alias = _preferences.ConversationNames.GetValueOrDefault(conversation.Key);
                var made = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), original, [pane], alias));
                created = made.Panes[0];
                // The tab goes to its folder's workspace, the strip following (the folder decides; docs/decisions/workspaces.md).
                PlaceTab(made);
            }
            ShowSavedPreview(created, conversation.Key);
            SetActivePane(created);
            SaveLayout();
            RefreshConversationRows();
            return Task.FromResult<SessionPane?>(created);
        }
        catch (Exception exception) { ShowError(exception.Message); return Task.FromResult<SessionPane?>(null); }
    }

    private async Task ResumeConversationAsync(Conversation conversation, StartRequest request, bool split = false)
    {
        var pane = await OpenConversationAsync(conversation, split);
        if (pane is not null && !pane.IsRunning) await ResumePaneAsync(pane, request);
    }

    /// <summary>
    /// Resumes the pane's conversation, or starts its shell. Every way of asking (the pane's button, Resume in Sessions, a
    /// double-click on the conversation, its right-click menu, Resume all) comes here, and a request made while the pane's
    /// resume is under way waits for it instead of making a second start. Returns whether the pane runs afterwards. What
    /// went wrong is shown as a notice, or handed to <paramref name="report"/> when the caller gathers several into one.
    /// </summary>
    /// <param name="again">Start the pane's session again under the identity it has (<see cref="StartAgainAsync"/>) rather than resume it.</param>
    private Task<bool> ResumePaneAsync(SessionPane pane, StartRequest request, Action<string>? report = null, bool again = false) => pane.ResumeOnceAsync(() => StartPaneAsync(pane, request, report, again));

    private async Task<bool> StartPaneAsync(SessionPane pane, StartRequest request, Action<string>? report = null, bool again = false)
    {
        void Report(string text) { if (report is null) ShowError(text); else report(text); }
        // Where this request spent its wait, for the notice should another start reach the pane meanwhile: the checks of the
        // saved history, and the refresh when the catalog as last read no longer matched them.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan? refresh = null;
        // Set once the terminal start begins: a failure before it belongs to a request that may have been abandoned since.
        var launching = false;
        try
        {
            if (pane.IsRunning) { await pane.Terminal!.FocusInputAsync(); return true; }
            // A pane or tab on its way out takes no new agent: its teardown would stop the agent without asking it to exit.
            if (Leaving(pane)) { UpdateStatus("That pane is closing"); return false; }
            if (LiveOrStarting(except: pane) >= ResumeAll.LiveLimit) throw new InvalidOperationException($"Stop a terminal before starting another. The limit is {ResumeAll.LiveLimit} panes running at once across a workspace's tabs.");
            var saved = pane.Descriptor;
            // A pane opened from Sessions took the folder known at that moment, none for a Hermes session bound before Hermes
            // wrote it, and kept it for good. The history or SIC's own record may know it by now; the history is refreshed once
            // when neither does yet. Only a recorded folder fills the pane, never a guess.
            if (saved.WorkingDirectory.Length == 0 && saved.SessionId is { } recordedId)
            {
                var recordedKey = TabLabels.Key(saved.AgentId, recordedId);
                var found = KnownFolder(_catalog.GetHeader(recordedKey)?.WorkingDirectory, recordedKey);
                if (found is null)
                {
                    var refreshing = System.Diagnostics.Stopwatch.StartNew();
                    await RefreshHistoryAsync();
                    refresh = refreshing.Elapsed;
                    found = KnownFolder(_catalog.GetHeader(recordedKey)?.WorkingDirectory, recordedKey);
                }
                if (found is not null)
                {
                    saved = saved with { WorkingDirectory = found };
                    pane.UpdateIdentity(saved);
                    pane.RefreshPreview();
                    SaveLayout();
                }
            }
            if (!Path.IsPathFullyQualified(saved.WorkingDirectory) || !Directory.Exists(saved.WorkingDirectory)) throw new DirectoryNotFoundException(FolderTrouble(saved));
            if (saved.AgentId == "shell")
            {
                launching = true;
                await StartTerminalAsync(pane, CreateShellRequest(NewShellArguments, saved.WorkingDirectory), request);
                RefreshConversationRows();
                return pane.IsRunning;
            }
            if (!AgentOn(saved.AgentId)) throw new InvalidOperationException(Theme.AgentName(saved.AgentId) + " is off in Settings → Agents.");
            var definition = _preferences.Agents.Single(agent => agent.Id == saved.AgentId) with { HistoryDirectory = saved.HistoryDirectory };
            if (again) { launching = true; return await StartAgainAsync(pane, definition, request, unsaved: false); }
            var id = saved.SessionId;
            // A pane an older SicTerm started under a creation name, and saved before it was linked, finds its session by that name.
            if (id is null && saved.CreationName is not null)
            {
                var matches = await Task.Run(() => HermesHistoryReader.ReadByTitle(definition, saved.CreationName).ToArray());
                if (matches.Length == 1)
                {
                    // Kept in saved too: the unsaved mark is cleared from it below, which put the pane back unlinked.
                    id = matches[0].SessionId;
                    saved = saved with { SessionId = id };
                    pane.UpdateIdentity(saved);
                    if (HostOf(pane) is { } named) AdoptIdentity(named, pane, TabLabels.Key(saved.AgentId, id));
                    SaveLayout();
                }
            }
            // A new session closed before its first message: nothing to resume, so it starts again (FreshStart).
            if (id is null && FreshStart.Replaces(saved, null)) { launching = true; return await StartAgainAsync(pane, definition, request, unsaved: true); }
            if (id is null) throw new InvalidOperationException("This tab is not linked to a saved conversation.");
            // The catalog as last read names the source, and the source names the session: no refresh is needed to resume
            // exactly. Only when that no longer holds is the history refreshed (a source that moved or appeared since) and
            // checked again; a refresh already under way is waited for rather than skipped.
            var key = TabLabels.Key(saved.AgentId, id);
            var verdict = await Task.Run(() => ResumeSource.Check(definition, _catalog.GetHeader(key), id));
            if (verdict != ResumeVerdict.Ready)
            {
                var refreshing = System.Diagnostics.Stopwatch.StartNew();
                await RefreshHistoryAsync();
                refresh = (refresh ?? TimeSpan.Zero) + refreshing.Elapsed;
                verdict = await Task.Run(() => ResumeSource.Check(definition, _catalog.GetHeader(key), id));
                if (verdict != ResumeVerdict.Ready && FreshStart.Replaces(saved, verdict))
                {
                    if (Leaving(pane) || CloseDecided(pane)) return false;
                    launching = true;
                    return await StartAgainAsync(pane, definition, request, unsaved: true);
                }
                if (verdict != ResumeVerdict.Ready)
                {
                    // The placeholder offers Start new session here from now on, beside the error.
                    if (verdict != ResumeVerdict.IdentityChanged) { pane.HistoryMissing = ResumeSource.Describe(verdict); pane.RefreshPreview(); }
                    throw new InvalidDataException(ResumeSource.Describe(verdict));
                }
            }
            // The pane, or its tab, was closed or began closing while this request waited, or SIC is quitting: there is nothing
            // left to start.
            if (Leaving(pane) || CloseDecided(pane)) return false;
            // Saved once the agent keeps a conversation in it. A Hermes session's row is there from its start, and Hermes deletes
            // an untitled one that ends with no messages, so a Hermes pane is saved once the catalog holds its conversation.
            if (saved.Unsaved && (saved.AgentId != "hermes" || _catalog.GetHeader(key) is not null)) { saved = saved with { Unsaved = false }; pane.UpdateIdentity(saved); }
            launching = true;
            // Off the UI thread: the user's status line is looked for in the folder, which may be on a slow network or WSL path.
            var limitArguments = await Task.Run(() => ClaudeLimitArguments(definition, saved.WorkingDirectory));
            await StartTerminalAsync(pane, AgentLaunchBuilder.Build(AgentDetection.PowerShellPath, definition, saved.WorkingDirectory, resumeId: id, extraArguments: limitArguments), request);
            SaveLayout();
            RefreshConversationRows();
            return pane.IsRunning;
        }
        catch (PaneStartedException started)
        {
            var conflict = StartConflictOf(pane, request, started, refresh, refresh is { } spent ? waited.Elapsed - spent : null);
            Diagnostics.Log("start conflict: " + conflict.Describe());
            Report(conflict.Summary);
        }
        // The pane or its tab closed, or SIC quit, while the start waited (a terminal waits for its tab to be shown): closing its
        // terminal ended the wait, and there is nothing to report.
        catch (OperationCanceledException) when (Leaving(pane) || CloseDecided(pane)) { }
        // The saved-history checks failed after the pane's close was decided: the request was abandoned. A terminal that fails
        // to start is still reported, and so is a failure while a close only waits on its question, which may yet be cancelled.
        catch (Exception) when (!launching && CloseDecided(pane)) { }
        catch (Exception exception) { Report(exception.Message); RefreshConversationRows(); }
        return pane.IsRunning;
    }

    /// <summary>
    /// Why the pane cannot start in its folder, and for a conversation where its folder is set: a conversation resumes only in
    /// the folder it ran in, so one SIC does not know is chosen with Link. "The saved working folder is missing or unknown" said neither.
    /// </summary>
    private static string FolderTrouble(SavedPane saved)
    {
        var shell = saved.AgentId == "shell";
        var what = shell ? "terminal" : "conversation";
        var trouble = saved.WorkingDirectory.Length == 0 ? $"The folder this {what} ran in is unknown." : $"The folder this {what} ran in no longer exists: {saved.WorkingDirectory}.";
        return shell ? trouble : trouble + " A conversation resumes only in its own folder, set with Link saved conversation.";
    }

    /// <summary>
    /// Starts the pane's terminal. A failure names the agent and tab, because it can be reported while another tab is showing:
    /// "The operation has timed out." alone did not say which of several resumes had failed.
    /// </summary>
    private async Task StartTerminalAsync(SessionPane pane, TerminalLaunchRequest launch, StartRequest request)
    {
        try { await pane.StartAsync(launch, request, () => !CloseDecided(pane)); }
        catch (Exception exception) when (exception is not (PaneStartedException or OperationCanceledException))
        {
            var host = HostOf(pane);
            var where = Theme.AgentName(pane.Descriptor.AgentId) + (host is null ? "" : " in “" + DisplayTitle(host.Saved) + "”");
            throw new InvalidOperationException(where + (pane.IsRunning ? " started, but its terminal reported: " : " did not start: ") + exception.Message, exception);
        }
    }

    /// <summary>
    /// Panes that hold a terminal or are getting one, for the running limit, counted over the current workspace's tabs since
    /// November 7, 2026 (docs/decisions/workspaces.md). A start that waits for its tab to be shown holds no process yet, but
    /// will, so it counts; <paramref name="except"/> is the pane asking.
    /// </summary>
    private int LiveOrStarting(SessionPane? except = null) =>
        StripHosts.SelectMany(host => host.Panes).Count(item => item != except && (item.IsRunning || item.Status == PaneStatus.Starting || item.Resuming));

    /// <summary>Whether the pane is gone, or its close or its tab's has begun.</summary>
    private bool Leaving(SessionPane pane) => HostOf(pane) is not { } host || host.Closing || _closingTabs.Contains(host.Item) || _closingPanes.Contains(pane);

    /// <summary>A request that found its pane started by another: both requests, the pane, and where this one waited, for the notice and the log.</summary>
    private StartConflict StartConflictOf(SessionPane pane, StartRequest request, PaneStartedException started, TimeSpan? refresh, TimeSpan? checks)
    {
        var host = HostOf(pane);
        return new StartConflict
        {
            Request = request, Holder = started.Holder, Running = started.Running, Agent = Theme.AgentName(pane.Descriptor.AgentId),
            Tab = host is null ? null : DisplayTitle(host.Saved), PaneNumber = host is null ? 1 : host.Panes.IndexOf(pane) + 1, PaneCount = host?.Panes.Count ?? 1,
            SessionId = pane.Descriptor.SessionId, ProcessId = started.ProcessId, ProcessStartedAt = started.ProcessStartedAt,
            Refresh = refresh, Checks = checks, Now = DateTimeOffset.UtcNow
        };
    }

    private async Task<SessionPane?> StartNewAsync(string agentId, string directory, SplitTarget? target = null, string? title = null)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) throw new DirectoryNotFoundException("The working folder must be an existing full path.");
        var definition = _preferences.Agents.FirstOrDefault(agent => agent.Id == agentId);
        if (definition is null && agentId != "shell") throw new InvalidOperationException(Theme.AgentName(agentId) + " is not set up in Settings.");
        if (!AgentOn(agentId)) throw new InvalidOperationException(Theme.AgentName(agentId) + " is off in Settings → Agents.");
        void CheckLimit() { if (LiveOrStarting() >= ResumeAll.LiveLimit) throw new InvalidOperationException($"The limit is {ResumeAll.LiveLimit} panes running at once across a workspace's tabs."); }
        CheckLimit();
        // A new Hermes session is found by its folder and the time it began, so it waits for one begun before it in the same
        // folder to be linked first (CreationTurns); its tab appears once it may start.
        CreationTurn? turn = null;
        if (agentId == "hermes")
        {
            try { turn = await TakeHermesTurnAsync(definition!, directory); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return null; }
        }
        // Set once the binding has the turn, which it ends; until then a failure ends it here.
        var linking = false;
        try
        {
            CheckLimit();
            var (id, name) = FreshStart.NewIdentity(agentId, DateTimeOffset.UtcNow);
            var saved = new SavedPane(Guid.NewGuid().ToString("N"), agentId, title ?? NewTitle(agentId, directory), directory, definition?.HistoryDirectory ?? "", id, name, Unsaved: agentId != "shell");
            var tabTitle = saved.Title;
            SessionPane pane;
            // A tab that began closing, or closed, while the new-session dialog was up, or whose pane to split has gone, gets a tab of its own instead.
            if (target is { Host: { Closing: false } into } && _open.ContainsKey(into.Item) && !_closingTabs.Contains(into.Item) && into.Panes.Count < PaneLayout.MaxPanes && into.Panes.Contains(target.Pane))
            {
                pane = AddPane(into, saved, target.Pane, target.Direction);
                _tabs.SelectedItem = into.Item;
            }
            else
            {
                var made = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), tabTitle, [saved]));
                pane = made.Panes[0];
                // The tab goes to its folder's workspace, the strip following (the folder decides; docs/decisions/workspaces.md).
                PlaceTab(made);
            }
            SetActivePane(pane);
            SaveLayout();
            var request = agentId == "shell" ? CreateShellRequest(NewShellArguments, directory) : await NewSessionLaunchAsync(definition!, saved);
            var launchedAt = DateTimeOffset.UtcNow;
            if (turn is not null) _hermesTurns.Launched(turn, launchedAt);
            try { await pane.StartAsync(request, new("a new session", launchedAt), () => !CloseDecided(pane)); }
            // Closed, or SIC quit, while its terminal waited for its tab to be shown: nothing started, nothing to report.
            catch (OperationCanceledException) when (Leaving(pane) || CloseDecided(pane)) { return null; }
            // Nothing began, so there is no conversation to come back to: a placeholder left behind would offer a Resume that
            // looks for a session that was never saved. The pane goes, and the error is reported by the caller.
            catch (Exception exception) when (!pane.IsRunning && HostOf(pane) is { } failed)
            {
                await ClosePaneAsync(failed, pane);
                throw new InvalidOperationException("The new " + Theme.AgentName(agentId) + " session did not start: " + exception.Message, exception);
            }
            if (agentId != "shell") { BindCreated(pane, definition!, launchedAt, turn); linking = true; }
            return pane;
        }
        finally { if (turn is not null && !linking) _hermesTurns.End(turn); }
    }

    /// <summary>
    /// A new Hermes session's turn in its folder (<see cref="CreationTurns"/>): at once, or once the session begun there before
    /// it has been linked, which the status bar says meanwhile. Cancelled when SicTerm quits.
    /// </summary>
    private Task<CreationTurn> TakeHermesTurnAsync(AgentDefinition agent, string directory)
    {
        var key = CreationTurns.Key(agent.HistoryDirectory, directory);
        if (!_hermesTurns.Busy(key)) return _hermesTurns.TakeAsync(key, CreationTurns.Wait, _lifetime.Token);
        var where = "New Hermes session in " + Paths.Leaf(directory);
        UpdateStatus(where + " · it starts once the one begun there before it is linked");
        return Waited();
        async Task<CreationTurn> Waited()
        {
            var turn = await _hermesTurns.TakeAsync(key, CreationTurns.Wait, _lifetime.Token);
            UpdateStatus(where);
            return turn;
        }
    }

    /// <summary>What a new session's pane and tab are called until the conversation has a title: the agent and the folder's name.</summary>
    private static string NewTitle(string agentId, string directory) => Theme.AgentName(agentId) + " · " + Paths.Leaf(directory);

    /// <summary>The launch of a new session under the pane's identity (<see cref="FreshStart.NewIdentity"/>).</summary>
    private async Task<TerminalLaunchRequest> NewSessionLaunchAsync(AgentDefinition definition, SavedPane saved)
    {
        // Off the UI thread: the user's status line is looked for in the folder, which may be on a slow network or WSL path.
        var limitArguments = await Task.Run(() => ClaudeLimitArguments(definition, saved.WorkingDirectory));
        return AgentLaunchBuilder.Build(AgentDetection.PowerShellPath, definition, saved.WorkingDirectory,
            newSessionId: saved.AgentId is "claude" or "opencode" ? saved.SessionId : null, extraArguments: limitArguments);
    }

    /// <summary>
    /// Links a new Codex or Hermes session to its saved conversation once the agent has written it. A Hermes session comes with
    /// its turn in its folder (<see cref="CreationTurns"/>), which ends with its linking.
    /// </summary>
    private void BindCreated(SessionPane pane, AgentDefinition definition, DateTimeOffset launchedAt, CreationTurn? turn = null)
    {
        if (turn is not null) _ = BindCreatedHermesAsync(pane, definition, launchedAt, turn);
        if (pane.Descriptor.AgentId == "codex")
        {
            UpdateStatus("New Codex session · it is linked to its saved conversation once the first message is sent");
            _ = BindCreatedCodexAsync(pane, definition, launchedAt);
        }
    }

    /// <summary>
    /// Starts the pane's session again in its folder, under the identity it has (<see cref="FreshStart"/>): an unsaved session
    /// on resume, or one Start new session here has just given a new identity. A pane without the identity its agent needs
    /// (a Claude Code or OpenCode pane with no session ID) is given one. Hermes begins a new session in its turn in the folder
    /// (<see cref="CreationTurns"/>), linked once Hermes has written it.
    /// </summary>
    /// <param name="unsaved">The pane is an unsaved session resumed, which the status bar explains; otherwise Start new session here.</param>
    private async Task<bool> StartAgainAsync(SessionPane pane, AgentDefinition definition, StartRequest request, bool unsaved)
    {
        var saved = pane.Descriptor;
        if (saved.AgentId is "claude" or "opencode" && saved.SessionId is null)
        {
            var (id, name) = FreshStart.NewIdentity(saved.AgentId, DateTimeOffset.UtcNow);
            saved = saved with { SessionId = id, CreationName = name };
            pane.UpdateIdentity(saved);
            SaveLayout();
        }
        // What Hermes begins now is a new session, found once it is written: a session the pane named, whose history is gone,
        // or the creation name an older SicTerm gave it, stands for nothing from here on.
        if (saved.AgentId == "hermes" && (saved.SessionId is not null || saved.CreationName is not null))
        {
            saved = saved with { SessionId = null, CreationName = null };
            pane.UpdateIdentity(saved);
            SaveLayout();
        }
        var turn = saved.AgentId == "hermes" ? await TakeHermesTurnAsync(definition, saved.WorkingDirectory) : null;
        var linking = false;
        try
        {
            var launch = await NewSessionLaunchAsync(definition, saved);
            var launchedAt = DateTimeOffset.UtcNow;
            if (turn is not null) _hermesTurns.Launched(turn, launchedAt);
            await StartTerminalAsync(pane, launch, request);
            BindCreated(pane, definition, launchedAt, turn);
            linking = true;
        }
        finally { if (turn is not null && !linking) _hermesTurns.End(turn); }
        SaveLayout();
        RefreshConversationRows();
        if (pane.IsRunning && unsaved) UpdateStatus(Theme.AgentName(saved.AgentId) + " resumed in " + Paths.Leaf(saved.WorkingDirectory) + " · no session history to restore");
        else if (pane.IsRunning && saved.AgentId != "codex") UpdateStatus("New " + Theme.AgentName(saved.AgentId) + " session in " + Paths.Leaf(saved.WorkingDirectory));
        return pane.IsRunning;
    }

    /// <summary>
    /// The placeholder's Start new session here: a new session of the pane's agent in the pane's folder, in the pane, under a
    /// new identity. The conversation the pane held, if any, is left in its history and in Sessions.
    /// </summary>
    private async Task StartNewHereAsync(SessionPane pane)
    {
        try
        {
            var saved = pane.Descriptor;
            if (!AgentOn(saved.AgentId)) throw new InvalidOperationException(Theme.AgentName(saved.AgentId) + " is off in Settings → Agents.");
            var definition = _preferences.Agents.Single(agent => agent.Id == saved.AgentId);
            var renewed = FreshStart.Renewed(saved, NewTitle(saved.AgentId, saved.WorkingDirectory), definition.HistoryDirectory, DateTimeOffset.UtcNow);
            pane.UpdateIdentity(renewed);
            if (HostOf(pane) is { } host)
            {
                host.Saved = host.Saved with { Panes = host.Panes.Select(item => item.Descriptor).ToList() };
                if (host.Panes.Count == 1) host.Saved = host.Saved with { Title = renewed.Title };
                host.Item.Tag = host.Saved;
                RefreshTabStatus(host);
                PaintTab(host);
            }
            pane.RefreshPreview();
            SaveLayout();
            await ResumePaneAsync(pane, new("the pane's Start new session here button", DateTimeOffset.UtcNow), again: true);
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    /// <summary>
    /// Codex names nothing at launch and writes its rollout, with the session ID, when the first message is sent. This watches
    /// the sessions home for the rollout of a session started in the pane's folder after the launch, for as long as the pane
    /// runs unlinked, and binds the pane to it, so the tab's name and color follow the conversation and it can be resumed.
    /// </summary>
    private async Task BindCreatedCodexAsync(SessionPane pane, AgentDefinition agent, DateTimeOffset launchedAt)
    {
        try
        {
            while (pane.IsRunning && pane.Descriptor.SessionId is null && !_lifetime.IsCancellationRequested)
            {
                await Task.Delay(2000, _lifetime.Token);
                if (!pane.IsRunning || pane.Descriptor.SessionId is not null) return;
                var directory = pane.Descriptor.WorkingDirectory;
                var bound = AllPanes.Select(item => item.Descriptor.SessionId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                var found = await Task.Run(() => CodexSessions.FindCreated(agent, directory, launchedAt, bound));
                if (found.Count == 0) continue;
                if (found.Count > 1) throw new InvalidDataException("More than one Codex session started in this folder. Link on the pane picks the right one.");
                if (!pane.IsRunning || pane.Descriptor.SessionId is not null) return;
                var created = found[0];
                pane.UpdateIdentity(pane.Descriptor with { SessionId = created.SessionId, HistoryDirectory = agent.HistoryDirectory });
                if (HostOf(pane) is { } host) { AdoptIdentity(host, pane, TabLabels.Key(agent.Id, created.SessionId)); RefreshTabStatus(host); PaintTab(host); }
                SaveLayout();
                UpdateStatus("Codex session linked · " + created.SessionId);
                await RefreshHistoryAsync();
                RefreshConversationRows();
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!_closing) ShowError("The new Codex session was not linked: " + exception.Message); }
    }

    /// <summary>
    /// Hermes names nothing at launch: a new session starts untitled, so that Hermes titles it and records its folder, and
    /// <c>hermes --resume</c> or <c>hermes -c "title"</c> find it outside SicTerm too (docs/decisions/hermes-session-names.md).
    /// Hermes writes the session's row a few seconds after its interface starts, before anything is sent (about 3 seconds for
    /// its command line and 9 for its TUI, seen November 7, 2026). This looks for that row, a terminal session begun in the
    /// pane's folder since the launch and before the next new session there, for as long as the pane runs unlinked, and binds
    /// the pane to it. The turn ends with it, so the next new Hermes session in the folder may start.
    /// </summary>
    private async Task BindCreatedHermesAsync(SessionPane pane, AgentDefinition agent, DateTimeOffset launchedAt, CreationTurn turn)
    {
        bool Unlinked() => !_lifetime.IsCancellationRequested && pane.IsRunning && pane.Descriptor.SessionId is null && HostOf(pane) is not null;
        try
        {
            var crowded = 0;
            for (var attempt = 0; Unlinked(); attempt++)
            {
                // Once a second while Hermes starts, then every five seconds: a Hermes held at its setup is linked once it begins.
                await Task.Delay(attempt < 60 ? 1000 : 5000, _lifetime.Token);
                if (!Unlinked()) return;
                var (folder, until) = (pane.Descriptor.WorkingDirectory, turn.Until);
                var found = await Task.Run(() => HermesHistoryReader.FindCreated(agent, folder, launchedAt.AddSeconds(-1), until));
                if (!Unlinked()) return;
                // A session another pane was linked to meanwhile is that pane's.
                var linked = AllPanes.Select(item => item.Descriptor.SessionId).OfType<string>().ToHashSet(StringComparer.Ordinal);
                var created = found.Where(conversation => !linked.Contains(conversation.SessionId)).ToArray();
                if (created.Length == 0) continue;
                // Another session begun here since the launch: Hermes started elsewhere in this folder, or a /new already typed.
                if (created.Length > 1)
                {
                    if (++crowded < 5) continue;
                    throw new InvalidDataException("More than one Hermes session started in this folder. Link on the pane picks the right one.");
                }
                // Hermes has written the session, so the pane resumes it exactly. It stays unsaved until it has a message: Hermes
                // deletes an untitled session that ends with none, and the pane then starts again (FreshStart). One already spoken
                // in is saved, as the catalog now holds it, so the pane can be pinned.
                var spoken = created[0].Body.Length > 0;
                pane.UpdateIdentity(pane.Descriptor with { SessionId = created[0].SessionId, Unsaved = pane.Descriptor.Unsaved && !spoken });
                if (HostOf(pane) is { } named) AdoptIdentity(named, pane, TabLabels.Key(agent.Id, created[0].SessionId));
                if (spoken) _catalog.Upsert(created[0]);
                SaveLayout(); return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!_closing) ShowError("The new Hermes session was not linked: " + exception.Message); }
        finally { _hermesTurns.End(turn); }
    }
}
