using Sic.Core.Appearance;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// Tab groups (docs/decisions/tab-groups.md): a split tab pinned whole. The tab is kept in settings.json as layout.json saves it,
/// under its own id, follows the open tab through every layout save, is listed under Pinned in Sessions, and comes back as a tab
/// from there. A tab of one pane pins its conversation instead (MainWindow.Close.cs). The pinned marks on tabs and panes are here too.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Whether the tab can be pinned: a split tab always, since its group keeps every pane; a tab of one pane when that pane has a conversation to pin.</summary>
    private bool CanPinTab(TabHost host) => TabGroups.IsGroup(host.Saved) || PinnableKeys(host).Length > 0;

    /// <summary>Whether the tab is pinned: as a group, or, with one pane, through its conversation.</summary>
    private bool IsTabPinned(TabHost host) => TabGroups.IsGroup(host.Saved)
        ? TabGroups.Contains(_preferences.TabGroups, host.Saved.Id)
        : PinnableKeys(host) is { Length: > 0 } keys && keys.All(_preferences.PinnedConversations.Contains);

    /// <summary>The tab as layout.json would save it now: its panes less any whose close is decided, and its tree, a divider drag under way included.</summary>
    private static SavedTab SavedNow(TabHost host)
    {
        var kept = host.Panes.Where(pane => !pane.Closing).ToList();
        return host.Saved with { Panes = kept.Select(pane => pane.Descriptor).ToList(), Layout = TreeWithout(host, kept), SplitRatio = null, RowRatio = null };
    }

    /// <summary>Pins the tab as a group, or brings its group up to date; saved with the preferences.</summary>
    private void PinGroup(SavedTab tab) { _preferences.TabGroups = TabGroups.Put(_preferences.TabGroups, tab); SavePreferences(); }

    private void UnpinGroup(string id) { _preferences.TabGroups = TabGroups.Remove(_preferences.TabGroups, id); SavePreferences(); }

    /// <summary>The open tab a group is, or null while it is closed.</summary>
    private TabHost? HostOfGroup(SavedTab group) => _open.Values.FirstOrDefault(host => host.Saved.Id == group.Id);

    /// <summary>
    /// Keeps every open pinned tab's group as the tab is, with each layout save: a split, a pane closed or moved out, a rename, a
    /// color or a divider drag reaches the group. A pinned tab down to one pane is no longer a group: its pin passes to the
    /// conversation left, when that pane has one, so the tab stays pinned. A tab between its panes leaving and its own removal (a
    /// drop into another tab) is left alone, and its group stays as the tab last stood.
    /// </summary>
    private void SyncGroups()
    {
        if (_closing) return;
        var saved = false; var relist = false;
        var pinsChanged = false;
        foreach (var host in _open.Values)
        {
            if (host.Closing || host.Panes.Count == 0 || TabGroups.Find(_preferences.TabGroups, host.Saved.Id) is not { } group) continue;
            var now = SavedNow(host);
            if (TabGroups.IsGroup(now))
            {
                if (TabGroups.Same(group, now)) continue;
                _preferences.TabGroups = TabGroups.Put(_preferences.TabGroups, now);
                relist |= TabGroups.Signature(group, _preferences.ConversationNames) != TabGroups.Signature(now, _preferences.ConversationNames);
            }
            else
            {
                _preferences.TabGroups = TabGroups.Remove(_preferences.TabGroups, host.Saved.Id);
                foreach (var key in PinnableKeys(host)) _preferences.PinnedConversations.Add(key);
                relist = true; pinsChanged = true;
            }
            saved = true;
        }
        if (!saved) return;
        SavePreferences();
        // A group brought up to date (a divider moved, a rename) pins nothing new; only a group dissolved into its conversation's pin does.
        if (pinsChanged) RefreshPinMarks();
        if (relist) _ = SearchAsync();
    }

    /// <summary>Sets each pane's pin button: after any pin changes, and for one tab from RefreshTabStatus. The tab itself carries no mark since October 16, 2026.</summary>
    private void RefreshPinMarks() { foreach (var host in _open.Values) RefreshPinMarks(host); }

    private void RefreshPinMarks(TabHost host)
    {
        if (host.Panes.Count == 0) return;
        // A tab pinned whole, as a group, takes its panes with it, so each of them shows the filled pin; a click on one unpins the tab (TogglePinPaneAsync).
        var tabPinned = TabGroups.IsGroup(host.Saved) && IsTabPinned(host);
        foreach (var pane in host.Panes)
        {
            // Judged once per pane per draw, and handed to the pane rather than asked for again by it.
            var key = PinnableKey(pane);
            pane.ShowPinned(tabPinned || (key is not null && _preferences.PinnedConversations.Contains(key)), key is not null);
        }
    }

    /// <summary>
    /// Opens a pinned tab as a tab again, as a launch restores one: every pane a placeholder, the tree, the name and the color as
    /// saved, under the group's own id, so the open tab keeps its group in step. A group whose tab is open is shown instead.
    /// Returns the tab, or null when it could not be opened.
    /// </summary>
    private TabHost? OpenGroup(SavedTab group)
    {
        try
        {
            if (HostOfGroup(group) is { } open) { ShowHost(open); SetActivePane(FocusedPane(open)); return open; }
            if (_tabs.TabItems.Count >= Workspaces.MaxTabs) throw new InvalidOperationException($"A workspace holds up to {Workspaces.MaxTabs} tabs.");
            if (group.Panes.Count is < 1 or > PaneLayout.MaxPanes) throw new InvalidDataException($"A pinned tab must hold one to {PaneLayout.MaxPanes} panes.");
            // The tab takes copies: a host replaces its saved record as the tab changes, and the group's own list stays the file's.
            var host = CreateTab(group with { Panes = group.Panes.ToList() }, select: true);
            SetActivePane(host.Panes[0]);
            SaveLayout(); RefreshConversationRows();
            return host;
        }
        catch (Exception exception) { ShowError(exception.Message); return null; }
    }

    /// <summary>
    /// Resumes a pinned tab: opens it (or shows it) and starts every pane that is not running, together, as Resume all starts a
    /// tab's panes. A conversation already running in another pane is left to that pane, since a second agent on one session would
    /// fight the first for it. One notice sums the run up, as Resume all's does.
    /// </summary>
    private async Task ResumeGroupAsync(SavedTab group, StartRequest request)
    {
        if (OpenGroup(group) is not { } host) return;
        var failures = new List<string>();
        var panes = new List<SessionPane>();
        foreach (var pane in host.Panes.Where(pane => !pane.IsRunning))
        {
            if (pane.ConversationKey is { } key && AllPanes.Any(other => other != pane && other.IsRunning && other.ConversationKey == key))
                failures.Add(Where(host, pane, "its conversation is already running in another tab."));
            else panes.Add(pane);
        }
        if (panes.Count == 0 && failures.Count == 0) return;
        var running = await Task.WhenAll(panes.Select(pane => ResumePaneAsync(pane, request, failure => failures.Add(Where(host, pane, failure)))));
        if (_closing) return;
        var started = running.Count(runs => runs);
        if (failures.Count == 0) NotifyBriefly(started == 1 ? "Resumed 1 session" : $"Resumed {started} sessions", "");
        else Notify(InfoBarSeverity.Warning, $"Resumed {started} of {panes.Count + failures.Count} sessions", string.Join("\n", failures));
    }

    /// <summary>Unpin from a group's row or its menu in Sessions: the group goes, and its open tab, if any, loses its mark.</summary>
    private async Task UnpinGroupAsync(SavedTab group)
    {
        UnpinGroup(group.Id);
        RefreshPinMarks();
        await SearchAsync();
        NotifyBriefly("Unpinned", TabLabels.TabTitle(group, _preferences.ConversationNames));
    }
}
