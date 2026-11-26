using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class FolderSoundsTests
{
    [Theory]
    [InlineData("tada", "tada")]
    [InlineData(" one-up.wav ", "one-up.wav")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("a\tb", null)]
    public void Normalize_TrimsAndRefusesNothingAndControlCharacters(string id, string? expected)
    {
        Assert.Equal(expected, FolderSounds.Normalize(id));
        Assert.Null(FolderSounds.Normalize(new string('x', FolderSounds.MaxLength + 1)));
    }

    [Fact]
    public void Clean_KeysFoldersAsPinsAreAndDropsWhatIsInvalid()
    {
        var clean = FolderSounds.Clean(new Dictionary<string, string> { ["C:\\Work\\Sic\\"] = " tada ", ["C:\\Bad"] = "", [""] = "ding" });
        Assert.Equal(["C:\\Work\\Sic"], clean.Keys);
        Assert.Equal("tada", clean["c:\\work\\sic"]);
    }

    [Fact]
    public void With_SetsAndClearsAFoldersSound()
    {
        var map = FolderSounds.With(null, "C:\\Work\\Sic", "coin");
        Assert.Equal("coin", map["C:\\Work\\Sic"]);
        map = FolderSounds.With(map, "C:\\Work\\Sic", "robot");
        Assert.Equal("robot", map["C:\\Work\\Sic"]);
        map = FolderSounds.With(map, "C:\\Work\\Sic", null);
        Assert.Empty(map);
        Assert.Empty(FolderSounds.With(null, "  ", "coin"));
    }

    [Fact]
    public void For_TakesTheNearestPinnedFolderWithASound()
    {
        string[] pinned = ["C:\\Work", "C:\\Work\\Sic", "C:\\Other"];
        var sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["C:\\Work"] = "bell", ["C:\\Other"] = "pew" };
        Assert.Equal("bell", FolderSounds.For(pinned, sounds, "C:\\Work\\Sic\\src"));   // Sic is pinned without a sound: its parent's.
        Assert.Equal("bell", FolderSounds.For(pinned, sounds, "c:\\work"));
        Assert.Equal("pew", FolderSounds.For(pinned, sounds, "C:\\Other\\deep\\er"));
        Assert.Null(FolderSounds.For(pinned, sounds, "D:\\Elsewhere"));
        Assert.Null(FolderSounds.For(pinned, sounds, null));
        sounds["C:\\Work\\Sic"] = "coin";
        Assert.Equal("coin", FolderSounds.For(pinned, sounds, "C:\\Work\\Sic\\src"));
        Assert.Equal("bell", FolderSounds.For(pinned, sounds, "C:\\Work\\Else"));
    }
}
