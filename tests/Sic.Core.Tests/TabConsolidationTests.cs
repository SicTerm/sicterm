using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TabConsolidationTests
{
    [Fact]
    public void TabsGroupByTheirFolderIgnoringCaseAndTrailingSeparators()
    {
        var tabs = new[] { ("a", @"C:\Repos\sic"), ("b", @"C:\Repos\docs"), ("c", @"c:\repos\SIC\"), ("d", ""), ("e", @"C:\Repos\docs"), ("f", @"C:\Repos\alone"), ("g", @"C:\Repos\sic") };
        var groups = TabConsolidation.Groups(tabs, tab => tab.Item2);
        Assert.Equal(2, groups.Count);
        Assert.Equal(["a", "c", "g"], groups[0].Select(tab => tab.Item1));
        Assert.Equal(["b", "e"], groups[1].Select(tab => tab.Item1));
    }

    [Fact]
    public void AFolderThatIsNoFullPathJoinsNoGroup()
    {
        Assert.Null(TabConsolidation.Key(""));
        Assert.Null(TabConsolidation.Key("   "));
        Assert.Null(TabConsolidation.Key(@"relative\folder"));
        Assert.Null(TabConsolidation.Key(null));
        Assert.Equal(@"C:\REPOS\SIC", TabConsolidation.Key(@"c:\Repos\..\Repos\sic\"));
        Assert.Empty(TabConsolidation.Groups(new[] { "x", "x" }, _ => "x"));
        Assert.Empty(TabConsolidation.Groups(new[] { @"C:\one", @"C:\two" }, folder => folder));
    }

    [Fact]
    public void TheLargestPaneIsWhereAConsolidatedTabGoesAndItSplitsAlongTheSideWithRoom()
    {
        var tree = PaneLayout.SplitNode(PaneSplit.Columns, 0.7, PaneLayout.Leaf("wide"), PaneLayout.SplitNode(PaneSplit.Rows, 0.5, PaneLayout.Leaf("b"), PaneLayout.Leaf("c")));
        var arrangement = PaneLayout.Arrange(tree, 1000, 600, 8);
        Assert.Equal("wide", PaneLayout.Largest(arrangement).Pane);
        // Equal panes: the first in the tree's order.
        Assert.Equal("a", PaneLayout.Largest(PaneLayout.Arrange(PaneLayout.SplitNode(PaneSplit.Columns, 0.5, PaneLayout.Leaf("a"), PaneLayout.Leaf("b")), 1000, 600, 8)).Pane);
        Assert.Equal(PaneSplit.Columns, PaneLayout.SplitThatFits(1000, 600, 8));
        Assert.Equal(PaneSplit.Rows, PaneLayout.SplitThatFits(300, 600, 8));
        // Wider than tall, but too narrow for two columns: rows, which have the room.
        Assert.Equal(PaneSplit.Rows, PaneLayout.SplitThatFits(300, 260, 8));
        Assert.Null(PaneLayout.SplitThatFits(300, 240, 8));
    }
}
