using Sic.App.Controls;
using Sic.Core.State;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>What a consolidation did: how many tabs joined others, in how many groups, and which tabs were left where they were, with why.</summary>
internal sealed record ConsolidationResult(int TabsMerged, int Groups, IReadOnlyList<string> LeftOut);

/// <summary>
/// Consolidate tabs by folder: every tab whose first pane works in the same folder as another's joins the first of them in
/// the strip, moved whole as a drop moves it (<see cref="MoveTabInto"/>), each onto the pane with the most room along that
/// pane's longer side, so the panes stay as square as the room allows. The rail's button and the tab menu's item, lit while
/// any two tabs share a folder. A tab that would take the target past sixteen panes, or finds no pane with the room to split,
/// stays where it is and is named in the notice. docs/decisions/tab-drops.md.
/// </summary>
internal sealed partial class MainWindow
{
    internal const string ConsolidateGlyph = "";

    /// <summary>The open tabs in strip order that are not closing (only those <paramref name="among"/> when given, for verification), by the folder of their first pane, in groups of two or more.</summary>
    private IReadOnlyList<IReadOnlyList<TabHost>> ConsolidationGroups(IReadOnlyCollection<TabHost>? among = null)
    {
        var tabs = _tabs.TabItems.OfType<TabViewItem>().Where(item => _open.TryGetValue(item, out var host) && !host.Closing && !_closingTabs.Contains(item) && (among is null || among.Contains(host))).Select(item => _open[item]);
        return TabConsolidation.Groups(tabs, host => host.Panes.Count > 0 ? host.Panes[0].Descriptor.WorkingDirectory : null);
    }

    private void ConsolidateTabsFromUser()
    {
        try
        {
            var result = ConsolidateTabs();
            if (result.Groups == 0) { NotifyBriefly("Nothing to consolidate", "No two tabs share a folder."); return; }
            var what = $"{result.TabsMerged + result.Groups} tabs merged into {result.Groups} by folder";
            if (result.LeftOut.Count == 0) NotifyBriefly("Tabs consolidated", what + ".");
            else Notify(InfoBarSeverity.Warning, "Tabs consolidated, some left out", what + ". Not moved: " + string.Join("; ", result.LeftOut) + ".");
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    /// <summary>
    /// Joins every group (<see cref="ConsolidationGroups"/>) into its first tab. Room is judged on the tab's tree laid out at the
    /// content area's size, since only the tab showing is laid out: the pane with the most room, split along its longer side
    /// when that has the room and the other way when only that has. The tab showing stays the one showing afterwards when it
    /// still exists, else the tab it joined.
    /// </summary>
    internal ConsolidationResult ConsolidateTabs(IReadOnlyCollection<TabHost>? among = null)
    {
        var showing = SelectedHost;
        var (width, height) = SelectedHost?.Content is { ActualWidth: > 0, ActualHeight: > 0 } laidOut ? (laidOut.ActualWidth, laidOut.ActualHeight)
            : (Math.Max(0, _work.ActualWidth), Math.Max(0, _work.ActualHeight - StripHeightNow));
        var merged = 0;
        var groups = 0;
        var leftOut = new List<string>();
        TabHost? joined = null;
        foreach (var group in ConsolidationGroups(among))
        {
            var target = group[0];
            var joinedHere = 0;
            foreach (var source in group.Skip(1))
            {
                if (target.Panes.Count + source.Panes.Count > PaneLayout.MaxPanes)
                {
                    leftOut.Add($"“{DisplayTitle(source.Saved)}” (“{DisplayTitle(target.Saved)}” would exceed {PaneLayout.MaxPanes} panes)");
                    continue;
                }
                var largest = PaneLayout.Largest(PaneLayout.Arrange(target.Content.Tree, width, height, PaneSplitter.GrabSize));
                if (PaneLayout.SplitThatFits(largest.Rect.Width, largest.Rect.Height, PaneSplitter.GrabSize) is not { } direction)
                {
                    leftOut.Add($"“{DisplayTitle(source.Saved)}” (no room to split in “{DisplayTitle(target.Saved)}”)");
                    continue;
                }
                if (ReferenceEquals(source, showing)) joined = target;
                MoveTabInto(source, target, largest.Pane, direction == PaneSplit.Columns ? PaneDirection.Right : PaneDirection.Down);
                merged++; joinedHere++;
            }
            if (joinedHere > 0) groups++;
        }
        if (showing is not null && _open.ContainsKey(showing.Item)) _tabs.SelectedItem = showing.Item;
        else if (joined is not null && _open.ContainsKey(joined.Item)) _tabs.SelectedItem = joined.Item;
        if (merged > 0) { SaveLayout(); UpdateStatus(); }
        Diagnostics.Log($"consolidate: {merged} tabs joined {groups} others; {leftOut.Count} left out");
        return new(merged, groups, leftOut);
    }
}
