using Sic.Core.Appearance;
using Sic.Core.Shell;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class JsonStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "command-central-state-tests-" + Guid.NewGuid().ToString("N"));
    public JsonStateStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Save_PreservesExactResumeDescriptorsAndSplitLayoutAcrossRestart()
    {
        var path = Path.Combine(_root, "layout.json");
        var store = new JsonStateStore<WorkspaceLayout>(path);
        var empty = store.Load(() => new WorkspaceLayout());
        Assert.Empty(empty.Tabs);
        var layout = new WorkspaceLayout
        {
            SelectedTabId = "tab-1",
            Tabs = [new("tab-1", "Workspace", [
                new("pane-1", "hermes", "First", _root, _root, "20260926_140330_dbff4d"),
                new("pane-2", "codex", "Unlinked new Codex", _root, _root)])]
        };
        store.Save(layout);
        var restored = new JsonStateStore<WorkspaceLayout>(path).Load(() => new WorkspaceLayout());
        Assert.Equal("tab-1", restored.SelectedTabId);
        Assert.Equal(layout.Tabs[0].Panes, Assert.Single(restored.Tabs).Panes);
        Assert.DoesNotContain("ProcessId", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Save_PreservesASixPaneTabsSplitTreeAndPaneNamesAndReadsOlderTabsFromTheirRatios()
    {
        var path = Path.Combine(_root, "layout.json");
        var panes = Enumerable.Range(1, 6).Select(index => new SavedPane("pane-" + index, "shell", "Pane " + index, _root, "", CustomTitle: index == 2 ? "Build watch" : null)).ToList();
        var tree = PaneLayout.SplitNode(PaneSplit.Columns, 0.4,
            PaneLayout.SplitNode(PaneSplit.Rows, 0.65, PaneLayout.Leaf("pane-1"), PaneLayout.SplitNode(PaneSplit.Columns, 0.5, PaneLayout.Leaf("pane-2"), PaneLayout.Leaf("pane-3"))),
            PaneLayout.SplitNode(PaneSplit.Rows, 0.5, PaneLayout.Leaf("pane-4"), PaneLayout.SplitNode(PaneSplit.Rows, 0.3, PaneLayout.Leaf("pane-5"), PaneLayout.Leaf("pane-6"))));
        new JsonStateStore<WorkspaceLayout>(path).Save(new WorkspaceLayout { Tabs = [new("tree", "Tree", panes, Layout: tree)] });
        var restored = Assert.Single(new JsonStateStore<WorkspaceLayout>(path).Load(() => new WorkspaceLayout()).Tabs);
        Assert.Equal(panes, restored.Panes);
        Assert.Equal(tree, restored.Layout);
        Assert.Equal("Build watch", restored.Panes[1].CustomTitle);
        Assert.Null(restored.SplitRatio);
        Assert.Null(restored.RowRatio);
        Assert.Equal(tree, PaneLayout.TreeOf(restored));
        var text = File.ReadAllText(path);
        Assert.Contains("\"Split\": \"columns\"", text);
        Assert.Contains("\"Split\": \"rows\"", text);

        // A file from before there were trees: the ratios stand in for one, and a pane without a name reads as unnamed.
        var folder = System.Text.Json.JsonSerializer.Serialize(_root);
        File.WriteAllText(path, $$"""
            { "Version": 1, "Tabs": [ { "Id": "t", "Title": "T", "SplitRatio": 0.3, "RowRatio": 0.7, "Panes": [
                { "Id": "a", "AgentId": "shell", "Title": "A", "WorkingDirectory": {{folder}}, "HistoryDirectory": "" },
                { "Id": "b", "AgentId": "shell", "Title": "B", "WorkingDirectory": {{folder}}, "HistoryDirectory": "" },
                { "Id": "c", "AgentId": "shell", "Title": "C", "WorkingDirectory": {{folder}}, "HistoryDirectory": "" } ] } ] }
            """);
        var older = Assert.Single(new JsonStateStore<WorkspaceLayout>(path).Load(() => new WorkspaceLayout()).Tabs);
        Assert.Equal(3, older.Panes.Count);
        Assert.Null(older.Layout);
        Assert.All(older.Panes, pane => Assert.Null(pane.CustomTitle));
        Assert.Equal(PaneLayout.SplitNode(PaneSplit.Columns, 0.3, PaneLayout.Leaf("a"), PaneLayout.SplitNode(PaneSplit.Rows, 0.7, PaneLayout.Leaf("b"), PaneLayout.Leaf("c"))), PaneLayout.TreeOf(older));
    }

    [Fact]
    public void Load_RejectsCorruptStateInsteadOfSilentlyDiscardingSavedSessions()
    {
        var path = Path.Combine(_root, "layout.json");
        File.WriteAllText(path, "{broken");
        Assert.Throws<InvalidDataException>(() => new JsonStateStore<WorkspaceLayout>(path).Load(() => new WorkspaceLayout()));
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Fact]
    public void Save_RetainsCustomTabNameAndConversationNameAcrossRestart()
    {
        var key = "claude:01234567-89ab-4def-8123-456789abcdef";
        var preferences = new AppPreferences { ConversationNames = new Dictionary<string, string> { [key] = "Blue Maple migration" } };
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(preferences);
        Assert.Equal("Blue Maple migration", new JsonStateStore<AppPreferences>(settings.FilePath)
            .Load(() => throw new Exception("Missing preferences")).ConversationNames[key]);

        var layout = new WorkspaceLayout { Tabs = [new SavedTab("tab", "Original history title",
            [new SavedPane("pane", "claude", "Original history title", _root, _root, key[7..])],
            CustomTitle: "Blue Maple migration")] };
        var tabs = new JsonStateStore<WorkspaceLayout>(Path.Combine(_root, "layout.json"));
        tabs.Save(layout);
        Assert.Equal("Blue Maple migration", Assert.Single(tabs.Load(() => throw new Exception("Missing layout")).Tabs).CustomTitle);
    }

    [Fact]
    public void Save_KeepsTheMotionChoiceAndTheFoldersOnceSuggestedAndReadsAnOlderFileWithoutThem()
    {
        var path = Path.Combine(_root, "settings.json");
        new JsonStateStore<AppPreferences>(path).Save(new AppPreferences { Motion = "off", AutoPinnedFolders = [@"C:\repos\sic", @"D:\notes"] });
        var restored = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal("off", restored.Motion);
        Assert.Equal([@"C:\repos\sic", @"D:\notes"], restored.AutoPinnedFolders);

        File.WriteAllText(path, "{\"Version\":1,\"Theme\":\"sic-dark\"}");
        var older = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal("on", older.Motion);
        Assert.Empty(older.AutoPinnedFolders);
        Assert.Null(older.PinnedFolders);
    }

    [Fact]
    public void Save_KeepsEachAgentsSwitchAndReadsAnOlderAgentWithoutOneAsOn()
    {
        var path = Path.Combine(_root, "settings.json");
        new JsonStateStore<AppPreferences>(path).Save(new AppPreferences
        {
            Agents = [new("codex", "Codex", "codex", [], @"C:\h\codex", false), new("opencode", "OpenCode", "opencode", [], @"C:\h\opencode", null)]
        });
        var restored = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal(new bool?[] { false, null }, restored.Agents.Select(agent => agent.Enabled));
        Assert.DoesNotContain("IsOn", File.ReadAllText(path));

        File.WriteAllText(path, """{"Version":1,"Agents":[{"Id":"hermes","Name":"Hermes","Executable":"hermes","Arguments":[],"HistoryDirectory":"C:\\h\\hermes"}]}""");
        var older = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.True(Assert.Single(older.Agents).IsOn);
    }

    [Fact]
    public void Save_KeepsTheSidebarSideAndTheReadyChimeAndReadsAnOlderFileWithTheRailLeftAndNoChime()
    {
        var path = Path.Combine(_root, "settings.json");
        new JsonStateStore<AppPreferences>(path).Save(new AppPreferences { Sidebar = "right", ReadyChime = true });
        var restored = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal("right", restored.Sidebar);
        Assert.True(restored.ReadyChime);

        File.WriteAllText(path, "{\"Version\":1,\"Theme\":\"sic-dark\"}");
        var older = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal("left", older.Sidebar);
        Assert.False(older.ReadyChime);
    }

    [Fact]
    public void Save_KeepsThePaneBorderWidthAndCloseAndMinimizeAndReadsA0926FileWithTheirOlderKeys()
    {
        var path = Path.Combine(_root, "settings.json");
        new JsonStateStore<AppPreferences>(path).Save(new AppPreferences { PaneBorderWidth = 6, CloseAndMinimize = "quit" });
        var text = File.ReadAllText(path);
        Assert.Contains("\"PaneBorderWidth\": 6", text);
        Assert.Contains("\"CloseAndMinimize\": \"quit\"", text);
        // The old word is cleared once read, and a null is not written.
        Assert.DoesNotContain("\"PaneBorder\"", text);
        var restored = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal(6, restored.PaneBorderWidth);
        Assert.Null(restored.PaneBorder);
        Assert.Equal("quit", restored.CloseAndMinimize);

        // As 0.9.26 and 0.9.27 wrote them: the width as a word, and the minimize switch, which is no longer read.
        File.WriteAllText(path, "{\"Version\":1,\"Theme\":\"sic-dark\",\"PaneBorder\":\"thick\",\"MinimizeToTray\":false}");
        var older = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal("thick", older.PaneBorder);
        Assert.Equal(PaneBorder.Default, older.PaneBorderWidth);
        Assert.Equal(3, PaneBorder.FromWord(older.PaneBorder));
        Assert.Equal(CloseAndMinimize.Tray, older.CloseAndMinimize);
    }

    [Fact]
    public void Save_KeepsPetImagesOffAndReadsAnOlderFileWithThemOn()
    {
        var path = Path.Combine(_root, "settings.json");
        Assert.True(new AppPreferences().PetImages);
        new JsonStateStore<AppPreferences>(path).Save(new AppPreferences { PetImages = false });
        Assert.Contains("\"PetImages\": false", File.ReadAllText(path));
        Assert.False(new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences")).PetImages);

        File.WriteAllText(path, "{\"Version\":1,\"Theme\":\"sic-dark\",\"CopyOnSelect\":true}");
        var older = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.True(older.PetImages);
        Assert.True(older.CopyOnSelect);
    }

    [Fact]
    public void Save_KeepsMultiLinePasteQuestionOffAndReadsAnOlderFileWithItOn()
    {
        var path = Path.Combine(_root, "settings.json");
        Assert.True(new AppPreferences().ConfirmMultiLinePaste);
        new JsonStateStore<AppPreferences>(path).Save(new AppPreferences { ConfirmMultiLinePaste = false });
        Assert.Contains("\"ConfirmMultiLinePaste\": false", File.ReadAllText(path));
        Assert.False(new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences")).ConfirmMultiLinePaste);

        File.WriteAllText(path, "{\"Version\":1,\"Theme\":\"sic-dark\",\"CopyOnSelect\":true}");
        Assert.True(new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences")).ConfirmMultiLinePaste);
    }

    [Fact]
    public void Save_RetainsTabColorConversationColorAndRecentCustomColorsAcrossRestart()
    {
        var key = "claude:01234567-89ab-4def-8123-456789abcdef";
        var preferences = new AppPreferences { ConversationColors = new Dictionary<string, string> { [key] = "blue" }, RecentTabColors = ["12AB34"] };
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(preferences);
        var loaded = new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal("blue", loaded.ConversationColors[key]);
        Assert.Equal(["12AB34"], loaded.RecentTabColors);

        var layout = new WorkspaceLayout { Tabs = [new SavedTab("tab", "Title", [new SavedPane("pane", "claude", "Title", _root, _root, key[7..])], Color: "12AB34")] };
        var tabs = new JsonStateStore<WorkspaceLayout>(Path.Combine(_root, "layout.json"));
        tabs.Save(layout);
        Assert.Equal("12AB34", Assert.Single(tabs.Load(() => throw new Exception("Missing layout")).Tabs).Color);
    }

    [Fact]
    public void Save_RetainsTheFolderSicKeptForAConversationAndOlderSettingsHaveNone()
    {
        var key = "hermes:20261006_134457_62b6bf";
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(new AppPreferences { ConversationFolders = new Dictionary<string, string> { [key] = _root } });
        Assert.Equal(_root, new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences")).ConversationFolders[key]);

        File.WriteAllText(settings.FilePath, """{ "Version": 1, "Theme": "sic-dark", "ConversationNames": {} }""");
        Assert.Empty(new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences")).ConversationFolders);
    }

    [Fact]
    public void Load_GivesSettingsWrittenBeforeTabColorsEmptyColorState()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{ "Version": 1, "Theme": "sic-dark", "ConversationNames": {} }""");
        var loaded = new JsonStateStore<AppPreferences>(path).Load(() => throw new Exception("Missing preferences"));
        Assert.Empty(loaded.ConversationColors);
        Assert.Empty(loaded.RecentTabColors);
        Assert.Null(loaded.Window);
        Assert.Equal(PanelLayout.DefaultWidth, loaded.PanelWidth);
        // A file from before the switch existed reads as on, the default for a new install since October 16, 2026.
        Assert.True(loaded.AlwaysExitCleanly);
        Assert.Empty(loaded.TabGroups);
    }

    [Fact]
    public void Save_RetainsAlwaysExitCleanlyAcrossRestart()
    {
        // On by default; a file that records it off stays off.
        Assert.True(new AppPreferences().AlwaysExitCleanly);
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(new AppPreferences { AlwaysExitCleanly = false });
        Assert.False(new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences")).AlwaysExitCleanly);
    }

    [Fact]
    public void Save_RetainsPinnedTabsWithTheirTreesAcrossRestart()
    {
        var panes = new List<SavedPane>
        {
            new("p1", "claude", "Claude Code · repo", @"C:\work\repo", @"C:\history", "s1", CustomTitle: "Schema"),
            new("p2", "shell", "PowerShell · repo", @"C:\work\repo", "")
        };
        var tree = new PaneNode(Split: "columns", Ratio: 0.4, First: new PaneNode("p1"), Second: new PaneNode("p2"));
        var group = new SavedTab("tab-1", "Claude Code · repo", panes, "Blue Maple", Color: "blue", Layout: tree);
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(new AppPreferences { TabGroups = [group] });
        var loaded = new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences"));
        var restored = Assert.Single(loaded.TabGroups);
        Assert.True(TabGroups.Same(group, restored));
        Assert.Equal("Schema", restored.Panes[0].CustomTitle);
        Assert.Equal(0.4, restored.Layout!.Ratio);
    }

    [Fact]
    public void Save_RetainsPanelWidthAcrossRestart()
    {
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(new AppPreferences { PanelWidth = 412 });
        var loaded = new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal(412, loaded.PanelWidth);
    }

    [Fact]
    public void Save_RetainsWindowPlacementAcrossRestart()
    {
        var settings = new JsonStateStore<AppPreferences>(Path.Combine(_root, "settings.json"));
        settings.Save(new AppPreferences { Window = new WindowPlacement(-1800, 40, 1600, 1000, true) });
        var loaded = new JsonStateStore<AppPreferences>(settings.FilePath).Load(() => throw new Exception("Missing preferences"));
        Assert.Equal(new WindowPlacement(-1800, 40, 1600, 1000, true), loaded.Window);
    }
}
