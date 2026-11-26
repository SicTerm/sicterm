using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

/// <summary>The folder lists settings.json kept before workspaces took them over (November 8, 2026): read in every shape they took, and never written again.</summary>
public sealed class WorkplaceFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sic-workplace-folder-tests-" + Guid.NewGuid().ToString("N"));
    public WorkplaceFoldersTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void ASavedListIsCleanedOnLoad()
    {
        Assert.Equal([@"C:\a", @"D:\b"], WorkplaceFolders.Clean([@"C:\a\", "", @"c:\A", "D:/b"]));
        Assert.Empty(WorkplaceFolders.Clean(null));
    }

    [Fact]
    public void Load_ReadsAFileFromTheRenameDaysWorkplacesAndPinsAsTheyWere()
    {
        var older = Load("""{ "Version": 1, "Workplaces": [ "D:\\notes", "C:\\repos" ], "PinnedFolders": [ "C:\\repos\\sic" ] }""");
        Assert.Equal([@"D:\notes", @"C:\repos"], older.Workplaces);
        Assert.Equal([@"C:\repos\sic"], older.PinnedFolders);
        Assert.Null(older.LegacyWorkspaces);
    }

    [Fact]
    public void Load_ReadsAnOlderFilesNamedWorkspacesAsTheirFolders()
    {
        var older = Load("""
            { "Version": 1, "Workspaces": [
                { "Id": "repositories", "Name": "Repositories", "Folders": [ "c:\\batch\\repos" ] },
                { "id": "two", "name": "Two roots", "folders": [ "D:\\a", "D:\\b\\" ] } ] }
            """);
        Assert.Equal([@"c:\batch\repos", @"D:\a", @"D:\b\"], older.LegacyWorkspaces);
        Assert.Null(older.Workplaces);
    }

    [Fact]
    public void Save_WritesNoneOfTheOldListsOnceTheyAreCleared()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new JsonStateStore<AppPreferences>(path);
        var older = Load("""{ "Version": 1, "Workplaces": [ "D:\\notes" ], "Workspaces": [ "C:\\repos" ], "PinnedFolders": [ "C:\\repos\\sic" ] }""");
        older.Workplaces = null; older.LegacyWorkspaces = null; older.PinnedFolders = null;
        store.Save(older);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("\"Workplaces\"", text);
        Assert.DoesNotContain("\"Workspaces\"", text);
        Assert.DoesNotContain("\"PinnedFolders\"", text);
        var fresh = store.Load(() => throw new Exception("Missing preferences"));
        Assert.Null(fresh.Workplaces); Assert.Null(fresh.LegacyWorkspaces); Assert.Null(fresh.PinnedFolders);
    }

    [Fact]
    public void Load_GivesAFileWithoutTheListsNothingToAdopt()
    {
        var fresh = Load("""{ "Version": 1, "Theme": "sic-dark" }""");
        Assert.Null(fresh.Workplaces);
        Assert.Null(fresh.LegacyWorkspaces);
        Assert.Null(fresh.PinnedFolders);
    }

    [Fact]
    public void Load_RejectsAWorkplaceThatIsNeitherAPathNorAGroup() =>
        Assert.Throws<InvalidDataException>(() => new JsonStateStore<AppPreferences>(WriteSettings("""{ "Version": 1, "Workplaces": [ 42 ] }""")).Load(() => new AppPreferences()));

    private AppPreferences Load(string json) => new JsonStateStore<AppPreferences>(WriteSettings(json)).Load(() => throw new Exception("Missing preferences"));

    private string WriteSettings(string json)
    {
        var path = Path.Combine(_root, "settings-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }
}
