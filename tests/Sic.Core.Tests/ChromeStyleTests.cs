using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ChromeStyleTests
{
    [Theory]
    [InlineData("solid", "solid")]
    [InlineData("frosted", "frosted")]
    [InlineData(" Clear ", "clear")]
    [InlineData("glass", "solid")]
    [InlineData("", "solid")]
    [InlineData(null, "solid")]
    public void AnUnknownWordLeavesTheChromeSolid(string? style, string expected) => Assert.Equal(expected, ChromeStyle.Normalize(style));

    [Fact]
    public void EveryChoiceIsItsOwnWordWithALabel_AndOnlySolidHidesTheBackdrop()
    {
        Assert.Equal(["solid", "frosted", "clear"], ChromeStyle.Choices);
        foreach (var choice in ChromeStyle.Choices)
        {
            Assert.Equal(choice, ChromeStyle.Normalize(choice));
            Assert.Equal(choice, ChromeStyle.Label(choice).ToLowerInvariant());
            Assert.Equal(choice != ChromeStyle.Solid, ChromeStyle.SeeThrough(choice));
        }
        Assert.False(ChromeStyle.SeeThrough("glass"));
    }
}
