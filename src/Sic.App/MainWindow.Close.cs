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

/// <summary>Closing panes, tabs and the window: the question about live agents, their exit, and what a Quit flushes.</summary>
internal sealed partial class MainWindow
{
    // Set once a Quit has been decided and its agents are being asked to exit; see CloseDecided.
    private bool _exitDecided;

    /// <summary>
    /// Whether the pane's close is decided (its question answered, or none asked): the pane or its tab is gone or exiting, or
    /// SIC is quitting. Unlike <see cref="Leaving"/>, a close still waiting on its question does not count, since it may be
    /// cancelled. A start that reaches its process after this has turned true is dropped, because the close has already asked
    /// the running agents to exit and would stop this one without asking.
    /// </summary>
    private bool CloseDecided(SessionPane pane) => _closing || _exitDecided || pane.Closing || HostOf(pane) is not { Closing: false };

    // Tabs and panes whose close is under way, question included: a second request for the same one (a double click on its
    // close button, Ctrl+Shift+W pressed again) returns at once instead of asking again or tearing the terminal down twice.
    private readonly HashSet<TabViewItem> _closingTabs = [];
    private readonly HashSet<SessionPane> _closingPanes = [];
    // A Quit waiting on its question or on its agents; a second Quit returns at once. A forced close still goes ahead.
    private bool _quitting;

    /// <summary>How long a live agent is given to end its own session before it is stopped.</summary>
    private static readonly TimeSpan ExitBudget = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How to close live agents: cleanly and without asking when Settings → Always exit cleanly is on; otherwise the Exit
    /// cleanly / Force / Cancel question, which the smoke test skips by stopping them at once.
    /// </summary>
    private async Task<CloseChoice> ChooseCloseAsync(string title, string message)
    {
        if (_preferences.AlwaysExitCleanly) return CloseChoice.Graceful;
        _closeQuestions++;
        if (_options.SmokeTest) return CloseChoice.Force;
        // WinUI shows one dialog at a time and throws on a second, which would end the app with every agent killed. With one
        // already up (Quit from the tray over New session, say), there is no asking, so the default answer stands.
        if (_dialogsOpen > 0) return CloseChoice.Graceful;
        return await AskCloseAsync(title, message + " Settings → Always exit cleanly skips this question.");
    }

    /// <summary>Closes that did not skip the question (under the smoke test, closes it answered with Force), for verification.</summary>
    private int _closeQuestions;

    private async Task ClosePaneAsync(TabHost host, SessionPane pane)
    {
        if (!host.Panes.Contains(pane) || _closingTabs.Contains(host.Item) || _closingPanes.Contains(pane)) return;
        // A pane whose close is under way counts as gone: two overlapping pane closes would otherwise each remove one and
        // leave the tab with none. Closing what is left closes the tab, which joins any exit already under way.
        if (!host.Panes.Any(other => other != pane && !_closingPanes.Contains(other))) { await CloseTabAsync(host.Item); return; }
        _closingPanes.Add(pane);
        try
        {
            if (pane.IsRunning)
            {
                var choice = await ChooseCloseAsync("Close this pane?", "Its agent is still running. Exit cleanly asks it to end its own session first so its history and locks are written; Force stops it at once. Saved agent history is never deleted.");
                if (choice == CloseChoice.Cancel) return;
                pane.Closing = true;
                if (choice == CloseChoice.Graceful) await ExitPanesAsync([pane]);
            }
            // The tab, or the whole app, may have closed while the agent was exiting; it took this pane with it.
            if (_closing || !_open.ContainsKey(host.Item) || !host.Panes.Contains(pane)) return;
            if (host.Panes.Count == 1) { await CloseTabAsync(host.Item); return; }
            await pane.DisposeAsync();
            host.Panes.Remove(pane);
            // The pane's room goes to the other side of its split; a drag under way on another divider is kept.
            SetTree(host, PaneLayout.Remove(host.Content.Tree, pane.Descriptor.Id)!);
            if (_activePane == pane) { _activePane = null; SetActivePane(host.Panes[0]); }
            RefreshTabStatus(host); SaveLayout(); UpdateStatus(); RefreshConversationRows();
            RefreshHistoryAfterClose([pane]);
        }
        finally { pane.Closing = false; _closingPanes.Remove(pane); }
    }

    private async Task CloseTabAsync(TabViewItem tab)
    {
        if (!_open.TryGetValue(tab, out var host) || !_closingTabs.Add(tab)) return;
        try
        {
            if (host.Panes.Any(pane => pane.IsRunning))
            {
                var choice = await ChooseCloseAsync("Close this tab?", "An agent is still running in it. Exit cleanly asks it to end its own session first so its history and locks are written; Force stops it at once. Saved agent history is never deleted.");
                if (choice == CloseChoice.Cancel) return;
                // Decided: a Quit during the wait below saves the layout without this tab, as it would have been saved after it.
                host.Closing = true;
                if (choice == CloseChoice.Graceful) await ExitPanesAsync(host.Panes.ToArray());
            }
            if (_closing || !_open.ContainsKey(tab)) return;
            // Side by side: each pane's teardown waits for its own process, and a tab of several waited for them in turn.
            await Task.WhenAll(host.Panes.ToArray().Select(pane => pane.DisposeAsync().AsTask()));
            if (host.Panes.Contains(_activePane!)) _activePane = null;
            _open.Remove(tab); RemoveTabItem(tab); SaveLayout(); UpdateStatus(); RefreshConversationRows();
            RefreshHistoryAfterClose(host.Panes);
        }
        finally { host.Closing = false; _closingTabs.Remove(tab); }
    }

    /// <summary>
    /// The conversation a pane can pin to the top of Sessions, or null: its agent's saved session. A new session nothing has been
    /// typed into, which the history does not have yet, is left out: no transcript is written for it, so its pin would answer to
    /// no row (docs/decisions/sessions-after-close.md). A shell has none. Judged from the pane alone: it is asked at every draw
    /// of a tab's status, and the catalog is consulted after each refresh instead (<see cref="NoteSavedSessionsAsync"/>), which
    /// marks a new session saved once the history holds it, as the Hermes binding does when it finds the session.
    /// </summary>
    private static string? PinnableKey(SessionPane pane) => pane.ConversationKey is { } key && !(pane.Descriptor.Unsaved && !pane.TypedInto) ? key : null;

    /// <summary>The conversations a tab holds that Sessions can pin, once each (<see cref="PinnableKey"/>).</summary>
    private static string[] PinnableKeys(TabHost host) => host.Panes.Select(PinnableKey).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Pins the conversations, saves the set, marks the tabs and panes, and lists Sessions again. One the catalog does not have
    /// yet (a session begun in this run) is read from the history in the background when <paramref name="refresh"/> is set; a
    /// close has already started that read.
    /// </summary>
    private async Task PinKeysAsync(IReadOnlyList<string> keys, bool refresh)
    {
        foreach (var key in keys) _preferences.PinnedConversations.Add(key);
        SavePreferences();
        RefreshPinMarks();
        await SearchAsync();
        if (refresh && !_closing && keys.Any(key => _catalog.GetHeader(key) is null)) _ = RefreshHistoryAfterCurrentAsync();
    }

    private async Task UnpinKeysAsync(IReadOnlyList<string> keys)
    {
        foreach (var key in keys) _preferences.PinnedConversations.Remove(key);
        SavePreferences();
        RefreshPinMarks();
        await SearchAsync();
    }

    /// <summary>
    /// Pin tab: a split tab is pinned whole, as a group (MainWindow.Groups.cs); a tab of one pane pins its conversation. The tab
    /// stays open, and the item reads Unpin tab while the tab is pinned. A brief notice says what was pinned, since the Sessions
    /// panel may be shut.
    /// </summary>
    internal async Task TogglePinTabAsync(TabViewItem tab)
    {
        if (!_open.TryGetValue(tab, out var host) || !CanPinTab(host)) return;
        var name = DisplayTitle(host.Saved);
        if (TabGroups.IsGroup(host.Saved))
        {
            if (IsTabPinned(host)) { UnpinGroup(host.Saved.Id); RefreshPinMarks(); await SearchAsync(); NotifyBriefly("Unpinned", name); return; }
            var group = SavedNow(host);
            PinGroup(group); RefreshPinMarks(); await SearchAsync();
            NotifyBriefly("Pinned to the top of Sessions", TabGroups.Label(group, _preferences.ConversationNames));
            return;
        }
        var keys = PinnableKeys(host);
        if (IsTabPinned(host)) { await UnpinKeysAsync(keys); NotifyBriefly("Unpinned", name); return; }
        await PinKeysAsync(keys, refresh: true);
        NotifyBriefly("Pinned to the top of Sessions", name);
    }

    /// <summary>
    /// Pin and close tab: the tab is pinned as Pin tab pins it, and closes. Pinned once the tab has closed, since the close may be
    /// cancelled at its question: a group as the tab stood before the close, a conversation whose row the refresh the close starts lists.
    /// </summary>
    internal async Task PinAndCloseTabAsync(TabViewItem tab)
    {
        if (!_open.TryGetValue(tab, out var host) || !CanPinTab(host)) return;
        var name = DisplayTitle(host.Saved);
        var group = TabGroups.IsGroup(host.Saved) ? SavedNow(host) : null;
        var keys = group is null ? PinnableKeys(host) : [];
        await CloseTabAsync(tab);
        if (_closing || _open.ContainsKey(tab)) return;
        if (group is not null)
        {
            PinGroup(group); await SearchAsync();
            NotifyBriefly("Pinned to the top of Sessions", TabGroups.Label(group, _preferences.ConversationNames));
            return;
        }
        await PinKeysAsync(keys, refresh: false);
        NotifyBriefly("Pinned to the top of Sessions", name);
    }

    /// <summary>
    /// Pin pane, and the header's pin button: the pane's conversation goes to the top of Sessions, or comes out of it when it is
    /// there (Unpin pane); the pane stays. In a tab pinned whole the button shows the tab's pin, so a click on it unpins the tab,
    /// as Unpin tab does, and leaves a pin of the conversation's own as it is.
    /// </summary>
    internal async Task TogglePinPaneAsync(SessionPane pane)
    {
        if (HostOf(pane) is { } owner && TabGroups.IsGroup(owner.Saved) && IsTabPinned(owner)) { await TogglePinTabAsync(owner.Item); return; }
        if (PinnableKey(pane) is not { } key)
        {
            // The header's pin is always there, so a click on a pane with nothing to pin says why rather than doing nothing.
            NotifyBriefly("Nothing to pin yet", pane.Descriptor.AgentId == "shell" ? "A PowerShell pane has no conversation to pin."
                : pane.ConversationKey is null ? "The pane is not linked to a saved conversation; Link attaches one."
                : "A new session is listed in Sessions, and can be pinned, once something has been sent in it.");
            return;
        }
        if (_preferences.PinnedConversations.Contains(key)) { await UnpinKeysAsync([key]); NotifyBriefly("Unpinned", pane.DisplayName); return; }
        await PinKeysAsync([key], refresh: true);
        NotifyBriefly("Pinned to the top of Sessions", pane.DisplayName);
    }

    /// <summary>
    /// Pin and close pane: the pane's conversation goes to the top of Sessions and this pane alone closes (its tab, when it is
    /// the tab's last). Pinned once the pane has closed, as a tab is, since the close may be cancelled at its question.
    /// </summary>
    internal async Task PinAndClosePaneAsync(TabHost host, SessionPane pane)
    {
        if (PinnableKey(pane) is not { } key) return;
        var name = pane.DisplayName;
        await ClosePaneAsync(host, pane);
        if (_closing || HostOf(pane) is not null) return;
        await PinKeysAsync([key], refresh: false);
        NotifyBriefly("Pinned to the top of Sessions", name);
    }

    /// <summary>
    /// After a tab or pane with a saved conversation closes, the history is read again in the background, so the conversation
    /// is in Sessions when it is looked for: a session started in this run is in no catalog until a refresh, which otherwise
    /// runs at launch. A refresh under way is let finish and one more runs after it, since it may have passed the transcript by.
    /// </summary>
    private void RefreshHistoryAfterClose(IEnumerable<SessionPane> panes)
    {
        if (_closing || !panes.Any(pane => pane.Descriptor.AgentId != "shell" && pane.Descriptor.SessionId is not null)) return;
        _ = RefreshHistoryAfterCurrentAsync();
    }

    /// <summary>Closes several tabs: side by side when no question will be asked, one after another when each may ask.</summary>
    private async Task CloseTabsAsync(IReadOnlyList<TabViewItem> tabs)
    {
        if (_preferences.AlwaysExitCleanly) { await Task.WhenAll(tabs.Select(CloseTabAsync)); return; }
        foreach (var tab in tabs) await CloseTabAsync(tab);
    }

    private async Task CloseAsync(bool force = false)
    {
        if (_closing || (_quitting && !force)) return;
        Diagnostics.Log("quit" + (force ? " (forced)" : "") + Environment.NewLine + Environment.StackTrace);
        if (!force && AllPanes.Any(pane => pane.IsRunning))
        {
            _quitting = true;
            try
            {
                var choice = await ChooseCloseAsync("Quit SicTerm?", "Agents are still running. Exit cleanly asks each one to end its own session first so their history and locks are written; Force stops them at once. Tabs are saved as inactive placeholders either way.");
                if (choice == CloseChoice.Cancel) return;
                // Decided: a start that reaches its process while the agents exit is dropped instead of being stopped unasked.
                _exitDecided = true;
                if (choice == CloseChoice.Graceful) await ExitWithProgressAsync(AllPanes.Where(pane => pane.IsRunning).ToArray());
            }
            finally { _quitting = false; _exitDecided = false; }
            // A forced close may have finished the job while this one waited on its agents.
            if (_closing) return;
        }
        _closing = true;
        // A notice's slide or hold under way ends here: a continuation that reached its storyboard after the window closed
        // ended SicTerm with an access violation (October 19, 2026).
        RestNotice();
        StopLimits();
        try
        {
            // The layout is written here, before the panes go, with any save still waiting its turn.
            SaveLayout(); FlushState(); _lifetime.Cancel(); _searchCancellation?.Cancel(); _presenceTimer?.Stop(); _updateTimer?.Stop();
            // Every pane at once: a teardown waits up to ten seconds for a process that will not end, and twelve in turn would be two minutes.
            await Task.WhenAll(AllPanes.ToArray().Select(pane => pane.DisposeAsync().AsTask()));
        }
        finally { _tray?.Dispose(); ApplyUpdateOnQuit(); _allowClose = true; Close(); }
    }

    /// <summary>
    /// Sends every live pane its agent's exit command and waits, in parallel, up to the budget, reporting each pane as it
    /// finishes. Whatever is still running afterwards is stopped by the caller's hard teardown. Returns how many were.
    /// </summary>
    private async Task<int> ExitPanesAsync(IReadOnlyList<SessionPane> panes, Action<SessionPane, bool>? finished = null)
    {
        var running = panes.Where(pane => pane.IsRunning).ToArray();
        if (running.Length == 0) return 0;
        UpdateStatus(running.Length == 1 ? "Asking the agent to exit…" : $"Asking {running.Length} agents to exit…");
        var results = await Task.WhenAll(running.Select(async pane =>
        {
            var exited = await pane.TryExitAsync(ExitBudget);
            finished?.Invoke(pane, exited);
            return exited;
        }));
        var stubborn = results.Count(exited => !exited);
        if (!_closing) UpdateStatus(stubborn == 0 ? "Agents exited cleanly" : $"{stubborn} still running after the exit request · stopping");
        return stubborn;
    }
}
