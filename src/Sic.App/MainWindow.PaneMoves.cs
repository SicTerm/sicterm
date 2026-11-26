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

/// <summary>Moving panes and tabs about: a tab into another tab, a pane out into a tab of its own, a pane into another tab or another side of its own.</summary>
internal sealed partial class MainWindow
{
    /// <summary>
    /// Moves every pane of <paramref name="source"/>, tiled as they are, into <paramref name="target"/> on <paramref name="side"/>
    /// of its pane <paramref name="paneId"/> (<see cref="PaneLayout.Insert"/>), and closes the source tab, now empty, without
    /// asking: nothing stops. The live terminals are moved as elements, from one canvas to the other, and keep running. The
    /// target keeps its name and its color: when it was called after its first pane and the drop puts another pane first, or
    /// colored after its one conversation, that name or color becomes the tab's own. The moved pane focused last takes the keyboard.
    /// </summary>
    internal void MoveTabInto(TabHost source, TabHost target, string paneId, PaneDirection side)
    {
        if (ReferenceEquals(source, target)) throw new InvalidOperationException("A tab cannot be moved into itself.");
        if (!_open.ContainsKey(source.Item) || !_open.ContainsKey(target.Item)) throw new InvalidOperationException("That tab is no longer open.");
        if (source.Closing || target.Closing || _closingTabs.Contains(source.Item) || _closingTabs.Contains(target.Item) || source.Panes.Any(pane => pane.Closing || _closingPanes.Contains(pane)))
            throw new InvalidOperationException("That tab is closing.");
        var tree = PaneLayout.Insert(target.Content.Tree, paneId, side, source.Content.Tree);
        var title = DisplayTitle(target.Saved);
        var color = OwnColor(target.Saved);
        var firstBefore = target.Saved.Panes[0].Id;
        if (source.Editor is not null) _ = EndRenameAsync(source, commit: false, refocus: false);
        var moved = source.Panes.ToArray();
        foreach (var pane in moved) { source.Content.Detach(pane); pane.Rechrome(target.Paint); }
        source.Panes.Clear();
        // Out of the open tabs before the selection moves: selecting the target refreshes every open tab, and the emptied source
        // has no first pane to refresh by (seen October 10, 2026, consolidating from a tab that was not the target).
        _open.Remove(source.Item);
        _tabs.SelectedItem = target.Item;
        RemoveTabItem(source.Item);
        target.Panes.AddRange(moved);
        var identity = target.Saved;
        if (identity.CustomTitle is null && PaneLayout.Leaves(tree)[0] != firstBefore) identity = identity with { CustomTitle = title };
        if (identity.Color is null && color is not null) identity = identity with { Color = color };
        target.Saved = identity;
        SetTree(target, tree);
        var now = DateTimeOffset.UtcNow;
        foreach (var pane in moved) pane.Presence.Selected(WindowInView, now);
        RefreshPresence(target); RefreshTabStatus(target); PaintTab(target);
        var focus = moved.MaxBy(pane => pane.LastActive)!;
        SetActivePane(focus);
        focus.FocusInput();
        SaveLayout(); UpdateStatus(); RefreshConversationRows();
    }

    /// <summary>
    /// Takes <paramref name="pane"/> out of its split tab into a tab of its own, right after that tab in the strip, with its
    /// terminal running as it was: the reverse of a drop. Its room goes to the other side of its split, as a close hands it
    /// over. The new tab has no name or color of its own: a tab of one pane is called after its pane, and takes the pane's
    /// conversation's color, as any tab of one pane does. The pane keeps the keyboard in its new tab. Nothing happens for a
    /// pane that is its tab's only one, which already has a tab of its own.
    /// </summary>
    internal TabHost? MovePaneToNewTab(SessionPane pane, int? at = null)
    {
        if (HostOf(pane) is not { } host) throw new InvalidOperationException("The pane is no longer open.");
        if (host.Panes.Count < 2) return null;
        if (host.Closing || _closingTabs.Contains(host.Item) || pane.Closing || _closingPanes.Contains(pane)) throw new InvalidOperationException("That pane is closing.");
        if (_tabs.TabItems.Count >= Workspaces.MaxTabs) throw new InvalidOperationException($"A workspace holds up to {Workspaces.MaxTabs} tabs.");
        TakePaneOut(host, pane);
        var index = _tabs.TabItems.IndexOf(host.Item);
        var moved = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), pane.Descriptor.Title, [pane.Descriptor]), select: true, at: at ?? (index < 0 ? null : index + 1),
            reuse: new Dictionary<string, SessionPane>(StringComparer.Ordinal) { [pane.Descriptor.Id] = pane });
        // A pane whose folder belongs to another workspace takes its new tab there, the strip following (the folder decides).
        PlaceTab(moved);
        SetActivePane(pane);
        pane.FocusInput();
        SaveLayout(); UpdateStatus(); RefreshConversationRows();
        return moved;
    }

    /// <summary>Takes a pane out of a tab of several, without disposing it: its room goes to the other side of its split, and the tab is retiled and repainted.</summary>
    private void TakePaneOut(TabHost host, SessionPane pane)
    {
        var remaining = PaneLayout.Remove(host.Content.Tree, pane.Descriptor.Id)!;
        host.Content.Detach(pane);
        host.Panes.Remove(pane);
        SetTree(host, remaining);
        if (_activePane == pane) _activePane = null;
        RefreshTabStatus(host);
    }

    /// <summary>
    /// Moves one pane to <paramref name="side"/> of pane <paramref name="targetPaneId"/> in <paramref name="target"/>, a drag of
    /// its header: within its own tab (the pane leaves its split and takes the half of the other pane, so a split made the wrong
    /// way is put right), or into another tab, where a pane that was its tab's only one takes its whole tab with it
    /// (<see cref="MoveTabInto"/>, which closes the emptied tab) and otherwise leaves its tab retiled. The target keeps its
    /// name and color as a drop keeps them. The pane keeps the keyboard. Nothing happens for a pane dropped on itself.
    /// </summary>
    internal void MovePaneInto(SessionPane pane, TabHost target, string targetPaneId, PaneDirection side)
    {
        if (HostOf(pane) is not { } home) throw new InvalidOperationException("The pane is no longer open.");
        if (!_open.ContainsKey(target.Item)) throw new InvalidOperationException("That tab is no longer open.");
        if (home.Closing || target.Closing || _closingTabs.Contains(home.Item) || _closingTabs.Contains(target.Item) || pane.Closing || _closingPanes.Contains(pane))
            throw new InvalidOperationException("That tab is closing.");
        if (ReferenceEquals(home, target))
        {
            if (targetPaneId == pane.Descriptor.Id) return;
            var title = DisplayTitle(home.Saved);
            var firstBefore = home.Saved.Panes[0].Id;
            var tree = PaneLayout.Insert(PaneLayout.Remove(home.Content.Tree, pane.Descriptor.Id)!, targetPaneId, side, PaneLayout.Leaf(pane.Descriptor.Id));
            if (home.Saved.CustomTitle is null && PaneLayout.Leaves(tree)[0] != firstBefore) home.Saved = home.Saved with { CustomTitle = title };
            SetTree(home, tree);
            RefreshTabStatus(home);
            SetActivePane(pane);
            pane.FocusInput();
            SaveLayout();
            return;
        }
        if (home.Panes.Count == 1) { MoveTabInto(home, target, targetPaneId, side); return; }
        var targetTitle = DisplayTitle(target.Saved);
        var targetColor = OwnColor(target.Saved);
        var targetFirst = target.Saved.Panes[0].Id;
        var joined = PaneLayout.Insert(target.Content.Tree, targetPaneId, side, PaneLayout.Leaf(pane.Descriptor.Id));
        TakePaneOut(home, pane);
        pane.Rechrome(target.Paint);
        target.Panes.Add(pane);
        var identity = target.Saved;
        if (identity.CustomTitle is null && PaneLayout.Leaves(joined)[0] != targetFirst) identity = identity with { CustomTitle = targetTitle };
        if (identity.Color is null && targetColor is not null) identity = identity with { Color = targetColor };
        target.Saved = identity;
        SetTree(target, joined);
        _tabs.SelectedItem = target.Item;
        pane.Presence.Selected(WindowInView, DateTimeOffset.UtcNow);
        RefreshPresence(target); RefreshTabStatus(target); PaintTab(target);
        SetActivePane(pane);
        pane.FocusInput();
        SaveLayout(); UpdateStatus(); RefreshConversationRows();
    }
}
