using Sic.Core.Appearance;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class FolderColorsTests
{
    [Theory]
    [InlineData("blue", "blue")]
    [InlineData("12ab34", "12AB34")]
    [InlineData("none", null)]
    [InlineData("Blue", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_KeepsColorsAndRefusesNoColor(string? color, string? expected) => Assert.Equal(expected, FolderColors.Normalize(color));

    [Fact]
    public void Clean_KeysFoldersAsPinsAreAndDropsWhatIsInvalid()
    {
        var clean = FolderColors.Clean(new Dictionary<string, string> { [@"C:\Work\Sic\"] = "abcdef", [@"C:\Bad"] = "teal", [@"C:\None"] = TabColors.None, [""] = "red" });
        Assert.Equal([@"C:\Work\Sic"], clean.Keys);
        Assert.Equal("ABCDEF", clean[@"c:\work\sic"]);
    }

    [Fact]
    public void With_SetsAndClearsAFoldersColor()
    {
        var map = FolderColors.With(null, @"C:\Work\Sic", "green");
        Assert.Equal("green", map[@"C:\Work\Sic"]);
        map = FolderColors.With(map, @"C:\Work\Sic", "123456");
        Assert.Equal("123456", map[@"C:\Work\Sic"]);
        map = FolderColors.With(map, @"C:\Work\Sic", null);
        Assert.Empty(map);
        Assert.Empty(FolderColors.With(null, "  ", "red"));
    }

    [Fact]
    public void For_TakesTheNearestPinnedFolderWithAColor()
    {
        string[] pinned = [@"C:\Work", @"C:\Work\Sic", @"C:\Other"];
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"C:\Work"] = "blue", [@"C:\Other"] = "pink" };
        Assert.Equal("blue", FolderColors.For(pinned, colors, @"C:\Work\Sic\src"));   // Sic is pinned without a color: its parent's.
        Assert.Equal("pink", FolderColors.For(pinned, colors, @"C:\Other\deep"));
        Assert.Null(FolderColors.For(pinned, colors, @"D:\Elsewhere"));
        Assert.Null(FolderColors.For([@"C:\Elsewhere"], colors, @"C:\Work"));          // Unpinned, the color is kept but not shown.
        colors[@"C:\Work\Sic"] = "orange";
        Assert.Equal("orange", FolderColors.For(pinned, colors, @"C:\Work\Sic\src"));
    }
}
