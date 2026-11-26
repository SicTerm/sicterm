using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class SectionFoldsTests
{
    [Fact]
    public void Clean_TrimsDropsBlanksAndTheKeysOfSectionsThatAreGone()
    {
        var clean = SectionFolds.Clean([" folders:pinned ", "", null!, "Folders:Workspaces", "folders:workplaces", "workspace:abc", "workspace-folders:abc", "folder:C:\\repos", " root:C:\\repos "]);
        Assert.Equal(["folder:C:\\repos", "root:C:\\repos"], clean.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains("ROOT:c:\\repos", clean);
    }

    [Fact]
    public void With_FoldsAndUnfoldsWithoutChangingTheSetGiven()
    {
        var folded = new HashSet<string> { "folder:C:\\repos" };
        var more = SectionFolds.With(folded, SectionFolds.Root("c:/repos/sic/"), true);
        Assert.Equal(["folder:C:\\repos", "root:c:\\repos\\sic"], more.Order());
        Assert.Single(folded);
        Assert.Empty(SectionFolds.With(more, "folder:C:\\repos", false).Except(["root:c:\\repos\\sic"]));
    }

    [Fact]
    public void Prune_DropsTheKeysOfFoldersNoLongerListedAndKeepsEveryOtherKey()
    {
        var folded = new[] { SectionFolds.Root(@"C:\kept"), SectionFolds.Root(@"C:\gone"), SectionFolds.Tabs(@"C:\kept\"), SectionFolds.Tabs(@"C:\gone"), SectionFolds.Tabs(Workspaces.ScratchId), "folder:C:\\repos", "today" };
        var pruned = SectionFolds.Prune(folded, [@"c:\KEPT\", @"D:\other"]);
        Assert.Equal(["folder:C:\\repos", "root:C:\\kept", "tabs:C:\\kept", "tabs:scratch", "today"], pruned.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFolderRowsKeyIsItsPathAsTheListKeepsIt() => Assert.Equal("root:C:\\repos", SectionFolds.Root("C:/repos/"));
}
