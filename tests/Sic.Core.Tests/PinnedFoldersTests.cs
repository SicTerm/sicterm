using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PinnedFoldersTests
{
    [Fact]
    public void PinningAddsTheFolderAtTheEndAndPinningAgainRemovesItHoweverItIsWritten()
    {
        var pinned = PinnedFolders.Toggle([], @"C:\repos\sic\");
        Assert.Equal([@"C:\repos\sic"], pinned);
        pinned = PinnedFolders.Toggle(pinned, @"D:\notes");
        Assert.Equal([@"C:\repos\sic", @"D:\notes"], pinned);
        Assert.True(PinnedFolders.Contains(pinned, "c:/REPOS/sic"));
        Assert.Equal([@"D:\notes"], PinnedFolders.Toggle(pinned, "c:/REPOS/sic/"));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("C:", @"C:\")]
    [InlineData(@"c:\work\", @"c:\work")]
    [InlineData(" C:/work/sub ", @"C:\work\sub")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void APathIsKeptAsWindowsWritesIt(string? path, string expected) => Assert.Equal(expected, PinnedFolders.Normalize(path));

    [Fact]
    public void ABlankPathChangesNothing()
    {
        Assert.Empty(PinnedFolders.Toggle([], "  "));
        Assert.Equal([@"C:\a"], PinnedFolders.Toggle([@"C:\a"], ""));
    }

    [Fact]
    public void ASavedListIsCleanedOnLoad()
    {
        Assert.Equal([@"C:\a", @"D:\b"], PinnedFolders.Clean([@"C:\a\", "", @"c:\A", "D:/b"]));
        Assert.Empty(PinnedFolders.Clean(null));
    }

    [Fact]
    public void SameIgnoresCaseSeparatorsAndTrailingSeparators()
    {
        Assert.True(PinnedFolders.Same(@"C:\Repos\SIC\", "c:/repos/sic"));
        Assert.False(PinnedFolders.Same(@"C:\repos\sic", @"C:\repos\sic-ops"));
    }
}
