using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class SidebarSideTests
{
    [Theory]
    [InlineData("right", "right")]
    [InlineData(" RIGHT ", "right")]
    [InlineData("left", "left")]
    [InlineData(null, "left")]
    [InlineData("", "left")]
    [InlineData("top", "left")]
    public void AnUnknownWordKeepsTheRailOnTheLeft(string? side, string expected) => Assert.Equal(expected, SidebarSide.Normalize(side));

    [Fact]
    public void EveryChoiceHasALabelAndNormalizesToItself()
    {
        foreach (var choice in SidebarSide.Choices)
        {
            Assert.Equal(choice, SidebarSide.Normalize(choice));
            Assert.NotEmpty(SidebarSide.Label(choice));
        }
        Assert.Equal("Left", SidebarSide.Label(null));
        Assert.True(SidebarSide.IsRight("Right"));
        Assert.False(SidebarSide.IsRight("sideways"));
    }
}
