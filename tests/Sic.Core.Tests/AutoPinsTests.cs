using Sic.Core.Sessions;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AutoPinsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static IEnumerable<(string? Folder, DateTimeOffset UpdatedAt)> Runs(string? folder, int count, int daysAgo = 1) =>
        Enumerable.Range(0, count).Select(index => (folder, Now.AddDays(-daysAgo).AddMinutes(-index)));

    private static List<string> Choose(IEnumerable<(string?, DateTimeOffset)> activity, IEnumerable<string>? pinned = null,
        IEnumerable<string>? offered = null, string? home = @"C:\Users\chris", Func<string, bool>? exists = null) =>
        AutoPins.Choose(AutoPins.Rank(activity, Now), pinned ?? [], offered ?? [], home, exists ?? (_ => true));

    [Fact]
    public void AFolderNeedsThreeConversationsWithinNinetyDaysAndTheBusiestComesFirst()
    {
        var activity = Runs(@"C:\repos\quiet", 2).Concat(Runs(@"C:\repos\sic", 5)).Concat(Runs(@"C:\repos\web", 3))
            .Concat(Runs(@"C:\repos\old", 6, daysAgo: 91)).Concat(Runs(@"C:\repos\edge", 2, daysAgo: 2)).Concat(Runs(@"C:\repos\edge", 1, daysAgo: 90));
        Assert.Equal([@"C:\repos\sic", @"C:\repos\web", @"C:\repos\edge"], Choose(activity));
    }

    [Fact]
    public void ATieGoesToTheFolderUsedMostRecently()
    {
        var activity = Runs(@"C:\repos\a", 3, daysAgo: 5).Concat(Runs(@"C:\repos\b", 3, daysAgo: 2));
        Assert.Equal([@"C:\repos\b", @"C:\repos\a"], Choose(activity));
    }

    [Fact]
    public void AFolderIsOneHoweverItIsWrittenAndIsSpelledAsMostRecently()
    {
        (string?, DateTimeOffset)[] activity = [(@"c:/repos/SIC/", Now.AddDays(-3)), (@"C:\repos\sic", Now.AddDays(-2)), (@"\\?\C:\repos\sic", Now.AddDays(-1))];
        var ranked = Assert.Single(AutoPins.Rank(activity, Now));
        Assert.Equal(new AutoPins.Candidate(@"C:\repos\sic", 3, Now.AddDays(-1)), ranked);
    }

    [Fact]
    public void RootsTheHomeFolderLinuxPathsAndMissingFoldersAreSkipped()
    {
        var activity = Runs(@"C:\", 9).Concat(Runs(@"\\server\share", 8)).Concat(Runs(@"C:\Users\Chris\", 7)).Concat(Runs("/home/chris/repo", 6))
            .Concat(Runs(@"E:\gone", 5)).Concat(Runs(null, 5)).Concat(Runs("", 5)).Concat(Runs(@"C:\repos\sic", 3));
        Assert.Equal([@"C:\repos\sic"], Choose(activity, exists: folder => !folder.StartsWith('E')));
    }

    [Fact]
    public void ExistingPinsStayAndAFolderInsideAPinOrAnEarlierChoiceIsSkipped()
    {
        var activity = Runs(@"C:\repos\sic", 9).Concat(Runs(@"C:\repos\sic\src", 8)).Concat(Runs(@"D:\notes\2026", 7))
            .Concat(Runs(@"C:\repos\sic-web", 3)).Concat(Runs(@"D:\notes", 2));
        Assert.Equal([@"C:\repos\sic", @"C:\repos\sic-web"], Choose(activity, pinned: [@"d:\NOTES\"]));
    }

    [Fact]
    public void AParentRankedBelowItsChosenSubfolderIsStillPinned()
    {
        var activity = Runs(@"C:\repos\sic\src", 5).Concat(Runs(@"C:\repos\sic", 3));
        Assert.Equal([@"C:\repos\sic\src", @"C:\repos\sic"], Choose(activity));
    }

    [Fact]
    public void AFolderPinnedBeforeAndSinceUnpinnedIsNotPinnedAgain()
    {
        var activity = Runs(@"C:\repos\sic", 5).Concat(Runs(@"C:\repos\web", 4));
        Assert.Equal([@"C:\repos\web"], Choose(activity, offered: [@"c:\repos\SIC\"]));
    }

    [Fact]
    public void AtMostEightAreAddedAndExistenceIsAskedOnlyOfThoseThatPassedTheRest()
    {
        var asked = new List<string>();
        var activity = Enumerable.Range(0, 12).SelectMany(index => Runs(@"C:\repos\p" + index.ToString("00"), 20 - index)).Concat(Runs(@"C:\", 30));
        var chosen = Choose(activity, exists: folder => { asked.Add(folder); return folder != @"C:\repos\p01"; });
        Assert.Equal(AutoPins.MaxAdded, chosen.Count);
        Assert.DoesNotContain(@"C:\repos\p01", chosen);
        Assert.Equal(@"C:\repos\p08", chosen[^1]);
        Assert.Equal(9, asked.Count);
        Assert.DoesNotContain(@"C:\", asked);
    }

    [Fact]
    public void TheCatalogGivesEachConversationsFolderAndTimeSinceADate()
    {
        var root = Path.Combine(Path.GetTempPath(), "sic-autopins-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ConversationCatalog(Path.Combine(root, "catalog.db"));
            Conversation Item(string id, string? folder, int daysAgo) =>
                new("claude", id, "t", folder, Now, Now.AddDays(-daysAgo), Path.Combine(root, id), root, "a long body");
            catalog.Upsert(Item("a", @"C:\repos\sic", 1));
            catalog.Upsert(Item("b", null, 1));
            catalog.Upsert(Item("c", @"C:\repos\old", 100));
            var activity = catalog.FolderActivity(Now - AutoPins.Window);
            Assert.Equal((@"C:\repos\sic", Now.AddDays(-1)), Assert.Single(activity));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
