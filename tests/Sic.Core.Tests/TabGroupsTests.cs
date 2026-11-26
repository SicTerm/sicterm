using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TabGroupsTests
{
    private static readonly Dictionary<string, string> NoNames = new(StringComparer.Ordinal);

    private static SavedPane Pane(string id, string agent = "claude", string? session = null, string folder = @"C:\work\repo", string? name = null) =>
        new(id, agent, agent == "shell" ? "PowerShell · repo" : "Claude Code · repo", folder, @"C:\history", session, CustomTitle: name);

    private static SavedTab Tab(string id, params SavedPane[] panes) => new(id, "Claude Code · repo", panes.ToList());

    [Fact]
    public void ATabOfTwoOrMorePanesIsAGroupAndOneOfOneIsNot()
    {
        Assert.False(TabGroups.IsGroup(Tab("a", Pane("p1"))));
        Assert.True(TabGroups.IsGroup(Tab("b", Pane("p1"), Pane("p2", "shell"))));
    }

    [Fact]
    public void CleanDropsWhatCannotBeAGroupAndSecondCopiesOfAnId()
    {
        var good = Tab("good", Pane("p1"), Pane("p2"));
        var single = Tab("single", Pane("p1"));
        var again = Tab("good", Pane("p3"), Pane("p4"));
        var noId = Tab("", Pane("p1"), Pane("p2"));
        var blankPane = Tab("blank", Pane("p1"), Pane(""));
        var tooMany = Tab("many", Enumerable.Range(0, PaneLayout.MaxPanes + 1).Select(index => Pane("p" + index)).ToArray());
        var clean = TabGroups.Clean([good, single, again, noId, blankPane, tooMany, null!]);
        Assert.Equal(["good"], clean.Select(group => group.Id));
        Assert.Same(good, clean[0]);
        Assert.Empty(TabGroups.Clean(null));
    }

    [Fact]
    public void PutReplacesTheGroupOfTheSameIdInPlaceAndAddsANewOneAtTheEnd()
    {
        var first = Tab("one", Pane("p1"), Pane("p2"));
        var second = Tab("two", Pane("p3"), Pane("p4"));
        var groups = TabGroups.Put([first, second], first with { CustomTitle = "Renamed" });
        Assert.Equal(["one", "two"], groups.Select(group => group.Id));
        Assert.Equal("Renamed", groups[0].CustomTitle);
        var added = TabGroups.Put(groups, Tab("three", Pane("p5"), Pane("p6")));
        Assert.Equal(["one", "two", "three"], added.Select(group => group.Id));
        Assert.Equal(["two"], TabGroups.Remove(groups, "one").Select(group => group.Id));
        Assert.True(TabGroups.Contains(groups, "two"));
        Assert.False(TabGroups.Contains(groups, "three"));
    }

    [Fact]
    public void SameComparesPanesAndTreeByValue()
    {
        var tree = new PaneNode(Split: "columns", Ratio: 0.5, First: new PaneNode("p1"), Second: new PaneNode("p2"));
        var tab = Tab("t", Pane("p1"), Pane("p2")) with { Layout = tree, Color = "blue" };
        var copy = tab with { Panes = tab.Panes.Select(pane => pane with { }).ToList(), Layout = tree with { } };
        Assert.True(TabGroups.Same(tab, copy));
        Assert.False(TabGroups.Same(tab, copy with { Color = "red" }));
        Assert.False(TabGroups.Same(tab, copy with { Layout = tree with { Ratio = 0.4 } }));
        Assert.False(TabGroups.Same(tab, copy with { Panes = [copy.Panes[0], copy.Panes[1] with { CustomTitle = "Other" }] }));
        Assert.False(TabGroups.Same(tab, copy with { Panes = [copy.Panes[0]] }));
    }

    [Fact]
    public void LabelsNameTheTabAndCountItsPanes()
    {
        var group = Tab("t", Pane("p1", folder: @"C:\work\blue-maple"), Pane("p2", "shell")) with { CustomTitle = "Migration" };
        Assert.Equal("Migration · 2 panes", TabGroups.Label(group, NoNames));
        Assert.Equal("2 panes · blue-maple", TabGroups.Summary(group));
        // Without a name of its own the group is called after its first pane, as a tab is.
        Assert.Equal("Claude Code · repo · 2 panes", TabGroups.Label(group with { CustomTitle = null }, NoNames));
    }

    [Fact]
    public void MatchesLooksAtTheTabsNameAndEveryPanesName()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { ["claude:s2"] = "Invoice parser" };
        var group = Tab("t", Pane("p1", name: "Schema review"), Pane("p2", session: "s2")) with { CustomTitle = "Blue Maple" };
        Assert.True(TabGroups.Matches(group, "maple", names));
        Assert.True(TabGroups.Matches(group, "schema", names));
        Assert.True(TabGroups.Matches(group, "invoice", names));
        Assert.True(TabGroups.Matches(group, "   ", names));
        Assert.False(TabGroups.Matches(group, "ledger", names));
    }

    [Fact]
    public void ShownFollowsTheAgentFilterTheFolderScopeAndTheHiddenAgents()
    {
        var group = Tab("t", Pane("p1", folder: @"C:\work\repo\src"), Pane("p2", "codex", folder: @"D:\other"));
        Assert.True(TabGroups.Shown(group, null, null, []));
        Assert.True(TabGroups.Shown(group, "codex", null, []));
        Assert.False(TabGroups.Shown(group, "hermes", null, []));
        Assert.True(TabGroups.Shown(group, null, @"C:\work\repo", []));
        Assert.False(TabGroups.Shown(group, null, @"C:\elsewhere", []));
        Assert.True(TabGroups.Shown(group, null, null, ["claude"]));
        Assert.False(TabGroups.Shown(group, null, null, ["claude", "codex"]));
        Assert.True(TabGroups.Shown(Tab("s", Pane("p1"), Pane("p2", "shell")), null, null, ["claude"]));
    }

    [Fact]
    public void TheSignatureChangesWithANameAPaneOrAColor()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var group = Tab("t", Pane("p1"), Pane("p2"));
        var signature = TabGroups.Signature(group, names);
        Assert.Equal(signature, TabGroups.Signature(group with { Panes = group.Panes.ToList() }, names));
        Assert.NotEqual(signature, TabGroups.Signature(group with { CustomTitle = "Named" }, names));
        Assert.NotEqual(signature, TabGroups.Signature(group with { Color = "red" }, names));
        Assert.NotEqual(signature, TabGroups.Signature(group with { Panes = [group.Panes[0]] }, names));
        names["claude:s"] = "Local";
        Assert.NotEqual(signature, TabGroups.Signature(Tab("t", Pane("p1", session: "s"), Pane("p2")), names));
    }
}
