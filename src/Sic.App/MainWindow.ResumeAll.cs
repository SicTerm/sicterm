using Sic.Core.Agents;
using Sic.Core.Sessions;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Resume all: every placeholder in every tab of every workspace from one request. A terminal loads only while its tab is
/// showing (TabView holds the selected tab's content alone, and a start in another tab waits for it), so the run shows each
/// workspace that has something to start, the current one first, and each of its tabs in turn for as long as its panes take
/// to start; it then comes back to the workspace and the tab it began on. One notice at the end says what started and what
/// did not, in place of a notice per pane: brief when everything started, held when anything fell short. Until November 9,
/// 2026 it took the current workspace only, while the rail's button was lit for a placeholder in any (Chris: "resume all for
/// all folders"). Since November 26, 2026 a run can take one workspace alone: Resume workspace tabs, on the workspace's
/// and the tab's menus and Ctrl+F5 (Chris: "resume only items in the selected workspace").
/// </summary>
internal sealed partial class MainWindow
{
    // One run at a time: a second request (the shortcut pressed again, the menu, the startup setting) joins the one under way.
    private readonly SingleFlight<bool> _resumeAll = new();

    internal bool ResumingAll => _resumeAll.Running;

    /// <summary>
    /// What a launch does with the placeholders it restored: resumes them when Settings → Resume all sessions on launch is on.
    /// Otherwise they wait, and the rail's Resume all button is lit for as long as any does.
    /// </summary>
    private void ResumeRestoredSessions()
    {
        if (_preferences.ResumeAllOnLaunch && AllPanes.Any(pane => !pane.IsRunning))
            _ = ResumeAllAsync(new("Resume all sessions on launch, in Settings", DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Resume all as the user asks for it (the rail button, F5, the tab menu), run as <see cref="ResumeAllAsync"/>. The panel stays
    /// as it is: with the tabs spread over workspaces, the Agent Workspaces panel is how the run is followed (it closed for the run until
    /// November 9, 2026).
    /// </summary>
    internal Task<bool> ResumeAllFromUserAsync(StartRequest request) => ResumeAllAsync(request);

    /// <summary>
    /// Resume workspace tabs (Ctrl+F5, the workspace's menu, the tab's menu): Resume all for one workspace's tabs, the rest
    /// left waiting. The run shows that workspace and stays on it: from another workspace's menu, the workspace asked for is the
    /// one the user is going to, as Consolidate tabs on the same menu goes there.
    /// </summary>
    internal Task<bool> ResumeWorkspaceFromUserAsync(Workspace? workspace, StartRequest request)
    {
        if (workspace is null || !_workspaces.Contains(workspace)) return Task.FromResult(false);
        if (!ReferenceEquals(workspace, _workspace)) SwitchWorkspace(workspace);
        return ResumeAllAsync(request, workspace);
    }

    /// <summary>Whether a workspace has a pane that is not running, for its menus' Resume workspace tabs.</summary>
    internal bool HasResumable(Workspace workspace) => HostsOf(workspace).Any(host => !host.Closing && host.Panes.Any(pane => !pane.IsRunning));

    /// <summary>
    /// Runs Resume all, for every workspace or for <paramref name="only"/>, or joins the run under way (whichever it takes). The
    /// rail button is greyed for the run and re-read after it.
    /// </summary>
    internal async Task<bool> ResumeAllAsync(StartRequest request, Workspace? only = null)
    {
        var run = _resumeAll.RunAsync(() => ResumeAllOnceAsync(request, only));
        UpdateStatus();
        try { return await run; }
        finally { if (!_closing) UpdateStatus(); }
    }

    /// <summary>Returns whether everything asked for started; a run cut short by a Quit, or with any failure, returns false.</summary>
    private async Task<bool> ResumeAllOnceAsync(StartRequest request, Workspace? only)
    {
        if (_closing || _quitting) return false;
        var taken = only is null ? _workspaces.ToList() : _workspaces.Where(workspace => ReferenceEquals(workspace, only)).ToList();
        if (taken.All(workspace => !HostsOf(workspace).Any()))
        {
            NotifyBriefly("Nothing to resume", only is null ? "No tabs are open." : "No tabs are open in " + WorkspaceName(only) + ".");
            return false;
        }
        var home = _workspace;
        var origin = _tabs.SelectedItem as TabViewItem;
        // Every workspace's tabs and panes as they are now, and its plan, the current workspace first and the rest in the panel's
        // order; a tab or pane closed during the run is passed over when its turn comes. The live limit is a workspace's (a
        // start counts the strip's, LiveOrStarting), so each plan has its own room, counted from its own tabs.
        var runs = new List<(Workspace Workspace, List<TabHost> Hosts, List<List<SessionPane>> Panes, ResumeAllPlan Plan)>();
        foreach (var workspace in taken.OrderBy(workspace => ReferenceEquals(workspace, home) ? 0 : 1))
        {
            var hosts = HostsOf(workspace).ToList();
            var panesOf = hosts.Select(host => host.Panes.ToList()).ToList();
            var selectedId = ReferenceEquals(workspace, home) ? (origin is not null && _open.TryGetValue(origin, out var shown) ? shown.Saved.Id : null) : workspace.SelectedTabId;
            var live = hosts.SelectMany(host => host.Panes).Count(pane => pane.IsRunning || pane.Status == PaneStatus.Starting || pane.Resuming);
            var plan = ResumeAll.Plan(hosts.Select((host, index) => (IReadOnlyList<PaneStartState>)panesOf[index].Select(pane => StateOf(host, pane)).ToList()).ToList(),
                hosts.FindIndex(host => host.Saved.Id == selectedId), live);
            runs.Add((workspace, hosts, panesOf, plan));
        }
        var requested = runs.Sum(run => run.Plan.Requested);
        var failures = new List<string>();
        var started = 0; var done = 0; var stopped = false;
        _notice.IsOpen = false;
        foreach (var (workspace, hosts, panesOf, plan) in runs.Where(run => run.Plan.Steps.Count > 0))
        {
            // A Quit decided meanwhile has asked the running agents to exit; one started now would be stopped unasked.
            if (_closing || _exitDecided) { stopped = true; break; }
            if (!_workspaces.Contains(workspace)) continue;
            // The workspace takes the strip for its turn, and gets back the tab it had selected when the turn is over.
            SwitchWorkspace(workspace, chosen: false);
            var selected = _tabs.SelectedItem as TabViewItem;
            foreach (var step in plan.Steps)
            {
                if (_closing || _exitDecided) { stopped = true; break; }
                var host = hosts[step.Tab];
                var panes = step.Panes.Select(index => panesOf[step.Tab][index]).Where(host.Panes.Contains).ToList();
                if (panes.Count == 0 || !_open.ContainsKey(host.Item) || host.Closing || _closingTabs.Contains(host.Item)) continue;
                // A tab moved to another workspace meanwhile (or a switch made by hand) is followed there.
                if (!_tabs.TabItems.Contains(host.Item) && WorkspaceOf(host) is { } now) SwitchWorkspace(now, chosen: false);
                UpdateStatus($"Resuming {done + 1}{(panes.Count > 1 ? "–" + (done + panes.Count) : "")} of {requested} · {DisplayTitle(host.Saved)}");
                if (!ReferenceEquals(_tabs.SelectedItem, host.Item)) _tabs.SelectedItem = host.Item;
                var running = await Task.WhenAll(panes.Select(pane => ResumePaneAsync(pane, request, failure => failures.Add(Where(host, pane, failure)))));
                started += running.Count(runs => runs);
                done += panes.Count;
            }
            if (selected is not null && _open.ContainsKey(selected) && _tabs.TabItems.Contains(selected) && !ReferenceEquals(_tabs.SelectedItem, selected)) _tabs.SelectedItem = selected;
            if (stopped) break;
        }
        if (_closing) return false;
        if (home is not null && _workspaces.Contains(home)) SwitchWorkspace(home, chosen: false);
        if (origin is not null && _open.ContainsKey(origin) && _tabs.TabItems.Contains(origin) && !ReferenceEquals(_tabs.SelectedItem, origin)) _tabs.SelectedItem = origin;
        var (title, message, trouble) = ResumeAll.Summarize(new(requested, started, failures, runs.Sum(run => run.Plan.Running), runs.Sum(run => run.Plan.Unlinked),
            runs.Sum(run => run.Plan.LeftOver), stopped, runs.Sum(run => run.Plan.AgentOff)));
        // A clean run's notice confirms what the tabs already show, so it passes; one that fell short stays until read.
        if (trouble) Notify(InfoBarSeverity.Warning, title, message); else NotifyBriefly(title, message);
        UpdateStatus(title);
        return !trouble;
    }

    private PaneStartState StateOf(TabHost host, SessionPane pane) =>
        pane.IsRunning || pane.Status == PaneStatus.Starting || pane.Resuming ? PaneStartState.Running
        : pane.Closing || host.Closing || _closingTabs.Contains(host.Item) || _closingPanes.Contains(pane) ? PaneStartState.Closing
        : !AgentOn(pane.Descriptor.AgentId) ? PaneStartState.AgentOff
        : pane.Descriptor.AgentId != "shell" && pane.Descriptor.SessionId is null && pane.Descriptor.CreationName is null && !pane.Descriptor.Unsaved ? PaneStartState.Unlinked
        : PaneStartState.Resumable;

    /// <summary>A failure as the summary lists it: which pane did not start, unless the message already names the tab (a terminal start's does).</summary>
    private string Where(TabHost host, SessionPane pane, string failure)
    {
        var tab = DisplayTitle(host.Saved);
        if (failure.Contains("“" + tab + "”", StringComparison.Ordinal)) return failure;
        return Theme.AgentName(pane.Descriptor.AgentId) + " in “" + tab + "”" + (host.Panes.Count > 1 ? ", pane “" + pane.Label + "”," : "") + " did not start: " + failure;
    }
}
