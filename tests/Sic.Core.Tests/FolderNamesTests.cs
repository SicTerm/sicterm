using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class FolderNamesTests
{
    [Theory]
    [InlineData("New folder")]
    [InlineData("  notes  ")]
    [InlineData(".config")]
    [InlineData("v1.2")]
    [InlineData("console")]
    [InlineData("été 2026")]
    public void ANameWindowsTakesHasNoProblem(string name) => Assert.Null(FolderNames.Problem(name));

    [Theory]
    [InlineData(null, "needs a name")]
    [InlineData("", "needs a name")]
    [InlineData("   ", "needs a name")]
    [InlineData("a/b", "cannot contain")]
    [InlineData("a\\b", "cannot contain")]
    [InlineData("what?", "cannot contain")]
    [InlineData("C:", "cannot contain")]
    [InlineData("tab\there", "cannot contain")]
    [InlineData("ends.", "cannot end with a period")]
    [InlineData("..", "cannot end with a period")]
    [InlineData("nul", "keeps the name NUL")]
    [InlineData("Com1.txt", "keeps the name COM1")]
    public void ANameWindowsRefusesSaysWhy(string? name, string reason) => Assert.Contains(reason, FolderNames.Problem(name));

    [Fact]
    public void ANameIsCutOffAtWhatNtfsKeeps()
    {
        Assert.Null(FolderNames.Problem(new string('a', FolderNames.MaxLength)));
        Assert.Contains("255", FolderNames.Problem(new string('a', FolderNames.MaxLength + 1)));
    }

    [Fact]
    public void CleanDropsTheSpacesAround() => Assert.Equal("notes", FolderNames.Clean("  notes "));

    [Fact]
    public void UnusedCountsUpFromTwoPastTheNamesTaken()
    {
        var taken = new HashSet<string> { "New folder", "New folder (2)" };
        Assert.Equal("New folder (3)", FolderNames.Unused(FolderNames.Default, taken.Contains));
        Assert.Equal("src", FolderNames.Unused("src", taken.Contains));
    }
}
