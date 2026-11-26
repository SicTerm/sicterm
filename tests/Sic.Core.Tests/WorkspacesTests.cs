using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class WorkspacesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-workspaces-tests-" + Guid.NewGuid().ToString("N"));
    public WorkspacesTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private static SavedTab Tab(string id, string folder) => new(id, "Tab " + id, [new SavedPane(id + "-pane", "claude", "Tab " + id, folder, @"C:\history")]);

    [Fact]
    public void Read_TakesAFileFromBeforeWorkspacesAsOneWorkspaceWithItsTabsAndSelection()
    {
        var layout = new WorkspaceLayout { SelectedTabId = "b", Tabs = [Tab("a", @"C:\repos\sic"), Tab("b", @"C:\repos\sic")] };
        var (workspaces, selected) = Workspaces.Read(layout);
        var only = Assert.Single(workspaces);
        Assert.Equal(selected, only.Id);
        Assert.Equal(["a", "b"], only.Tabs.Select(tab => tab.Id));
        Assert.Equal("b", only.SelectedTabId);
        Assert.Null(only.Home);
    }

    [Fact]
    public void Read_KeepsTheSavedWorkspacesInOrderAndTheSavedChoice()
    {
        var layout = new WorkspaceLayout
        {
            SelectedWorkspaceId = "two",
            Workspaces = [new("one", [Tab("a", @"C:\repos\sic")], Home: "c:/repos/sic/"), new("two", [], SelectedTabId: null)]
        };
        var (workspaces, selected) = Workspaces.Read(layout);
        Assert.Equal(["one", "two"], workspaces.Select(workspace => workspace.Id));
        Assert.Equal("two", selected);
        Assert.Equal(@"c:\repos\sic", workspaces[0].Home);
        Assert.Empty(workspaces[1].Tabs);
    }

    [Fact]
    public void Read_FallsBackToTheFirstWorkspaceWhenTheChoiceIsGone()
    {
        var (workspaces, selected) = Workspaces.Read(new WorkspaceLayout { SelectedWorkspaceId = "gone", Workspaces = [new("one", []), new("two", [])] });
        Assert.Equal("one", selected);
        Assert.Equal(2, workspaces.Count);
    }

    [Fact]
    public void Read_GivesAnEmptyListOneEmptyWorkspace()
    {
        var (workspaces, selected) = Workspaces.Read(new WorkspaceLayout { Workspaces = [] });
        var only = Assert.Single(workspaces);
        Assert.Equal(only.Id, selected);
        Assert.Empty(only.Tabs);
    }

    [Fact]
    public void Read_RefusesAWorkspaceWithoutAnIdOrWithASecondCopyOfOne()
    {
        Assert.Throws<InvalidDataException>(() => Workspaces.Read(new WorkspaceLayout { Workspaces = [new(" ", [])] }));
        Assert.Throws<InvalidDataException>(() => Workspaces.Read(new WorkspaceLayout { Workspaces = [new("one", []), new("one", [])] }));
        Assert.Throws<InvalidDataException>(() => Workspaces.Read(new WorkspaceLayout { Workspaces = Enumerable.Range(0, Workspaces.MaxWorkspaces + 1).Select(index => new SavedWorkspace("w" + index, [])).ToList() }));
    }

    [Fact]
    public void RootFor_IsTheDeepestListedFolderThatHoldsTheFolder()
    {
        string[] folders = [@"C:\repos", @"C:\repos\sic\", "d:/notes"];
        Assert.Equal(@"C:\repos", Workspaces.RootFor(folders, @"C:\repos\web\src"));
        Assert.Equal(@"C:\repos\sic", Workspaces.RootFor(folders, @"c:\REPOS\sic\tests"));
        Assert.Equal(@"C:\repos\sic", Workspaces.RootFor(folders, @"C:\repos\sic"));
        Assert.Equal(@"d:\notes", Workspaces.RootFor(folders, @"D:\notes\daily"));
        Assert.Null(Workspaces.RootFor(folders, @"C:\reposit\x"));
        Assert.Null(Workspaces.RootFor(folders, @"E:\elsewhere"));
        Assert.Null(Workspaces.RootFor(folders, ""));
        Assert.Null(Workspaces.RootFor(folders, null));
        Assert.Null(Workspaces.RootFor([], @"C:\repos"));
    }

    [Fact]
    public void AWorkspaceIsNamedAfterItsFolderAndScratchHasNoFolder()
    {
        Assert.Equal("sic", Workspaces.Name(new("w", [], Home: @"C:\repos\sic")));
        Assert.Equal(@"C:\", Workspaces.Name(new("w", [], Home: @"C:\")));
        Assert.Equal("Scratch", Workspaces.Name(new(Workspaces.ScratchId, [Tab("a", @"C:\repos\sic")])));
        Assert.Equal(@"C:\repos\sic", Workspaces.IdFor("c:/repos/sic/".Replace("c:", "C:")));
        Assert.Equal(@"c:\repos\sic", Workspaces.NormalizeHome("c:/repos/sic/"));
        Assert.Null(Workspaces.NormalizeHome(" "));
    }

    [Fact]
    public void TheSummaryCountsTabsAndNamesTheFolder()
    {
        Assert.Equal("No tabs", Workspaces.Summary(new(Workspaces.ScratchId, [])));
        Assert.Equal("2 tabs", Workspaces.Summary(new(Workspaces.ScratchId, [Tab("a", @"C:\x"), Tab("b", @"D:\y")])));
        Assert.Equal(@"No tabs · D:\notes", Workspaces.Summary(new("w", [], Home: @"D:\notes")));
        Assert.Equal(@"1 tab · C:\repos\sic", Workspaces.Summary(new("w", [Tab("a", @"C:\repos\sic")], Home: @"C:\repos\sic")));
    }

    [Fact]
    public void Regroup_PutsEveryTabInTheWorkspaceOfItsDeepestFolderAndTheRestInScratchLast()
    {
        List<SavedWorkspace> saved =
        [
            new("one", [Tab("a", @"C:\repos\web"), Tab("b", @"C:\repos\sic\src"), Tab("c", @"E:\elsewhere")], SelectedTabId: "b"),
            new("two", [Tab("d", @"D:\notes\daily"), Tab("e", @"C:\repos"), new SavedTab("f", "No folder", [new SavedPane("f-pane", "shell", "PowerShell", "", "")])], SelectedTabId: "d")
        ];
        var (workspaces, selected) = Workspaces.Regroup(saved, "one", [@"C:\repos", @"c:\repos\SIC", @"D:\notes"]);
        Assert.Equal([@"C:\repos", @"c:\repos\SIC", @"D:\notes", "scratch"], workspaces.Select(workspace => workspace.Id));
        Assert.Equal([@"C:\repos", @"c:\repos\SIC", @"D:\notes", null], workspaces.Select(workspace => workspace.Home));
        Assert.Equal(["a", "e"], workspaces[0].Tabs.Select(tab => tab.Id));
        Assert.Equal(["b"], workspaces[1].Tabs.Select(tab => tab.Id));
        Assert.Equal(["d"], workspaces[2].Tabs.Select(tab => tab.Id));
        Assert.Equal(["c", "f"], workspaces[3].Tabs.Select(tab => tab.Id));
        // Each shows the tab that was showing where it came from, else its first; the current workspace is where the showing tab landed.
        Assert.Equal(["a", "b", "d", "c"], workspaces.Select(workspace => workspace.SelectedTabId));
        Assert.Equal(@"c:\repos\SIC", selected);
    }

    [Fact]
    public void Regroup_KeepsScratchOnlyWhileItHoldsATabOrThereAreNoFolders()
    {
        var (grouped, _) = Workspaces.Regroup([new("one", [Tab("a", @"C:\repos\sic")])], "one", [@"C:\repos"]);
        Assert.Equal([@"C:\repos"], grouped.Select(workspace => workspace.Id));
        var (noFolders, selected) = Workspaces.Regroup([new("one", [Tab("a", @"C:\repos\sic")])], "one", []);
        var only = Assert.Single(noFolders);
        Assert.Equal(Workspaces.ScratchId, only.Id);
        Assert.Equal(Workspaces.ScratchId, selected);
        Assert.Equal(["a"], only.Tabs.Select(tab => tab.Id));
        var (empty, first) = Workspaces.Regroup([], "none", [@"C:\repos"]);
        Assert.Equal([@"C:\repos"], empty.Select(workspace => workspace.Id));
        Assert.Empty(empty[0].Tabs);
        Assert.Null(empty[0].SelectedTabId);
        Assert.Equal(@"C:\repos", first);
    }

    [Fact]
    public void Regroup_PicksTheCurrentWorkspaceByItsHomeWhenItShowedNoTabAndTheFirstOtherwise()
    {
        var (byHome, selected) = Workspaces.Regroup([new("one", [Tab("a", @"C:\repos")]), new("two", [], Home: @"D:\notes\inner")], "two", [@"C:\repos", @"D:\notes"]);
        Assert.Equal(@"D:\notes", selected);
        Assert.Equal(2, byHome.Count);
        var (_, fallback) = Workspaces.Regroup([new("one", [Tab("a", @"C:\repos")]), new("two", [], Home: @"E:\gone")], "two", [@"C:\repos", @"D:\notes"]);
        Assert.Equal(@"C:\repos", fallback);
    }

    [Fact]
    public void AdoptFolders_FillsTheListOnceFromTheHomesThenTheOldListsAndClearsThem()
    {
        List<SavedWorkspace> saved = [new("open", [Tab("a", @"C:\repos\sic")], Home: @"C:\repos\sic"), new("two", [], Home: "c:/REPOS/sic"), new("none", [])];
        var preferences = new AppPreferences { PinnedFolders = [@"D:\notes\", @"C:\repos\web"], Workplaces = [@"c:\REPOS\web", @"C:\batch\repos"], LegacyWorkspaces = [@"E:\ignored"] };
        Assert.True(Workspaces.AdoptFolders(saved, preferences));
        Assert.Equal([@"C:\repos\sic", @"D:\notes", @"C:\repos\web", @"C:\batch\repos"], preferences.Folders);
        Assert.Null(preferences.PinnedFolders); Assert.Null(preferences.Workplaces); Assert.Null(preferences.LegacyWorkspaces);
        Assert.False(Workspaces.AdoptFolders(saved, preferences));
        Assert.Equal(4, preferences.Folders!.Count);
    }

    [Fact]
    public void AdoptFolders_ReadsTheOlderWorkspacesKeyWhenThereIsNoWorkplacesListAndAFreshFileGetsAnEmptyList()
    {
        var preferences = new AppPreferences { LegacyWorkspaces = [@"c:\repos\SIC\", @"D:\notes"] };
        Assert.True(Workspaces.AdoptFolders([new("one", [Tab("a", @"C:\repos\sic")])], preferences));
        Assert.Equal([@"c:\repos\SIC", @"D:\notes"], preferences.Folders);
        var fresh = new AppPreferences();
        Assert.True(Workspaces.AdoptFolders([new("fresh", [])], fresh));
        Assert.Empty(fresh.Folders!);
        Assert.False(Workspaces.AdoptFolders([], fresh));
    }

    [Fact]
    public void AdoptFolders_AddsAnOldListFoundBesideAFilledListOnceAndStopsAtTheLimit()
    {
        var preferences = new AppPreferences { Folders = [@"C:\a"], Workplaces = [@"C:\b"] };
        Assert.True(Workspaces.AdoptFolders([], preferences));
        Assert.Equal([@"C:\a", @"C:\b"], preferences.Folders);
        var many = new AppPreferences { PinnedFolders = Enumerable.Range(0, Workspaces.MaxWorkspaces + 3).Select(index => @"C:\f" + index).ToList() };
        Assert.True(Workspaces.AdoptFolders([], many));
        Assert.Equal(Workspaces.MaxWorkspaces, many.Folders!.Count);
    }

    [Fact]
    public void ADigitPicksItsWorkspaceAndNinePicksTheLast()
    {
        Assert.Equal(0, Workspaces.IndexForDigit(1, 3));
        Assert.Equal(2, Workspaces.IndexForDigit(3, 3));
        Assert.Equal(2, Workspaces.IndexForDigit(5, 3));
        Assert.Equal(11, Workspaces.IndexForDigit(9, 12));
        Assert.Equal(-1, Workspaces.IndexForDigit(1, 0));
    }

    [Fact]
    public void Save_WritesTheWorkspacesAndTheCurrentOnesTabsBesideThemAndReadsBoth()
    {
        var path = Path.Combine(_root, "layout.json");
        var store = new JsonStateStore<WorkspaceLayout>(path);
        var current = new SavedWorkspace(@"D:\notes", [Tab("b", @"D:\notes")], Home: @"D:\notes", SelectedTabId: "b");
        store.Save(new WorkspaceLayout
        {
            SelectedWorkspaceId = @"D:\notes", SelectedTabId = "b", Tabs = current.Tabs,
            Workspaces = [new(@"C:\repos\sic", [Tab("a", @"C:\repos\sic")], Home: @"C:\repos\sic", SelectedTabId: "a"), current]
        });
        var text = File.ReadAllText(path);
        Assert.Contains("\"Workspaces\": [", text);
        Assert.Contains("\"SelectedWorkspaceId\": \"D:\\\\notes\"", text);
        Assert.Contains("\"Home\": \"C:\\\\repos\\\\sic\"", text);
        Assert.DoesNotContain("\"Name\"", text);
        var loaded = store.Load(() => throw new Exception("Missing layout"));
        var (workspaces, selected) = Workspaces.Read(loaded);
        Assert.Equal(@"D:\notes", selected);
        Assert.Equal([@"C:\repos\sic", @"D:\notes"], workspaces.Select(workspace => workspace.Id));
        Assert.Equal(["b"], loaded.Tabs.Select(tab => tab.Id));
        // A file from before there were workspaces has neither key.
        store.Save(new WorkspaceLayout { Tabs = current.Tabs, SelectedTabId = "b" });
        Assert.DoesNotContain("Workspaces", File.ReadAllText(path));
        Assert.DoesNotContain("SelectedWorkspaceId", File.ReadAllText(path));
    }

    [Fact]
    public void Save_WritesTheFolderListAndAFileWithoutOneReadsAsNull()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new JsonStateStore<AppPreferences>(path);
        store.Save(new AppPreferences { Folders = [@"C:\repos", @"D:\notes"] });
        Assert.Contains("\"Folders\": [", File.ReadAllText(path));
        Assert.Equal([@"C:\repos", @"D:\notes"], store.Load(() => throw new Exception("Missing preferences")).Folders);
        File.WriteAllText(path, """{ "Version": 1, "Theme": "sic-dark" }""");
        Assert.Null(store.Load(() => throw new Exception("Missing preferences")).Folders);
    }
}
