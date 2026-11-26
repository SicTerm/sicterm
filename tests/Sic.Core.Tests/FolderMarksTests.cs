using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class FolderMarksTests
{
    [Theory]
    [InlineData("🍁", "🍁")]
    [InlineData(" 🍁 ", "🍁")]
    [InlineData("👩‍💻", "👩‍💻")]      // A joiner sequence is one text element.
    [InlineData("👍🏽", "👍🏽")]       // A skin tone too.
    [InlineData("🇨🇦", "🇨🇦")]        // And a flag, two regional indicators.
    [InlineData("A", "A")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ab", null)]
    [InlineData("🍁🍂", null)]
    [InlineData("a\tb", null)]
    [InlineData("\u0007", null)]
    public void Normalize_KeepsOneTextElementAndRefusesTheRest(string mark, string? expected)
    {
        Assert.Equal(expected, FolderMarks.Normalize(mark));
    }

    [Theory]
    [InlineData("🍁🍂", "🍂")]
    [InlineData("🍁", "🍁")]
    [InlineData("x👩‍💻", "👩‍💻")]
    [InlineData("", null)]
    public void Last_TakesTheLastTextElementTypedSoANewEmojiReplacesTheOld(string typed, string? expected)
    {
        Assert.Equal(expected, FolderMarks.Last(typed));
    }

    [Fact]
    public void Clean_KeysFoldersAsPinsAreAndDropsInvalidMarks()
    {
        var clean = FolderMarks.Clean(new Dictionary<string, string>
        {
            ["c:/batch/repos/"] = "🍁", ["C:\\Other\\"] = "ab", [""] = "🍂", ["C:\\Notes"] = " 📓 "
        });
        Assert.Equal(new Dictionary<string, string> { [@"c:\batch\repos"] = "🍁", [@"C:\Notes"] = "📓" }, clean);
        Assert.Equal("🍁", clean[@"C:\BATCH\REPOS"]);
    }

    [Fact]
    public void With_MarksAndUnmarksAFolderHoweverItsPathIsWritten()
    {
        var marked = FolderMarks.With(null, @"C:\Batch\Repos\", "🍁");
        Assert.Equal("🍁", marked[@"c:\batch\repos"]);
        var replaced = FolderMarks.With(marked, @"c:/batch/repos", "🍂");
        Assert.Equal(["🍂"], replaced.Values);
        Assert.Empty(FolderMarks.With(replaced, @"C:\Batch\Repos", null));
        Assert.Empty(FolderMarks.With(replaced, @"C:\Batch\Repos", "not one"));
        Assert.Equal(replaced, FolderMarks.With(replaced, "  ", "🍁"));
    }

    [Theory]
    [InlineData(@"C:\Batch\Repos", @"C:\Batch\Repos", true)]
    [InlineData(@"C:\Batch\Repos", @"c:/batch/repos/sic/src", true)]
    [InlineData(@"C:\Batch\Repos\", @"C:\Batch\Repos\sic", true)]
    [InlineData(@"C:\Batch\Repos", @"C:\Batch\Repositories", false)]
    [InlineData(@"C:\Batch\Repos", @"C:\Batch", false)]
    [InlineData(@"C:\", @"C:\Batch", true)]
    [InlineData("", @"C:\Batch", false)]
    [InlineData(@"C:\Batch", "", false)]
    public void IsUnder_IsTheFolderOrInsideIt(string folder, string path, bool expected)
    {
        Assert.Equal(expected, FolderMarks.IsUnder(folder, path));
    }

    [Fact]
    public void For_TakesTheNearestMarkedPinnedFolderAboveTheWorkingFolder()
    {
        string[] pinned = [@"C:\Batch", @"C:\Batch\Repos\sic", @"C:\Batch\Repos"];
        var marks = FolderMarks.Clean(new Dictionary<string, string> { [@"C:\Batch"] = "📁", [@"C:\Batch\Repos"] = "🍁" });
        Assert.Equal("🍁", FolderMarks.For(pinned, marks, @"C:\Batch\Repos\sic\src"));   // The pinned sic has no mark, so Repos's shows.
        Assert.Equal("🍁", FolderMarks.For(pinned, marks, @"c:/batch/repos"));
        Assert.Equal("📁", FolderMarks.For(pinned, marks, @"C:\Batch\Other"));
        Assert.Null(FolderMarks.For(pinned, marks, @"D:\Elsewhere"));
        Assert.Null(FolderMarks.For(pinned, marks, ""));
        Assert.Null(FolderMarks.For(pinned, marks, null));
        // An unpinned folder's mark, kept in the file, does not show.
        Assert.Null(FolderMarks.For([@"C:\Batch\Repos\sic"], marks, @"C:\Batch\Repos\sic"));
        // A map read without the comparer is keyed as pins are, compared without case, once cleaned, which the app does on load.
        Assert.Equal("🍁", FolderMarks.For(pinned, FolderMarks.Clean(new Dictionary<string, string> { [@"c:\batch\repos"] = "🍁" }), @"C:\Batch\Repos\sic"));
    }
}
