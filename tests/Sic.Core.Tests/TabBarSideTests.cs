using Sic.Core.Appearance;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TabBarSideTests
{
    [Theory]
    [InlineData("bottom", "bottom")]
    [InlineData(" BOTTOM ", "bottom")]
    [InlineData("top", "top")]
    [InlineData(null, "top")]
    [InlineData("", "top")]
    [InlineData("left", "top")]
    public void AnUnknownWordKeepsTheTabsAtTheTop(string? side, string expected) => Assert.Equal(expected, TabBarSide.Normalize(side));

    [Fact]
    public void EveryChoiceHasALabelAndNormalizesToItself()
    {
        foreach (var choice in TabBarSide.Choices)
        {
            Assert.Equal(choice, TabBarSide.Normalize(choice));
            Assert.NotEmpty(TabBarSide.Label(choice));
        }
        Assert.Equal("Top", TabBarSide.Label(null));
        Assert.True(TabBarSide.IsBottom("Bottom"));
        Assert.False(TabBarSide.IsBottom("sideways"));
    }

    [Fact]
    public void ASettingsFileWithoutTheWordKeepsTheTabsAtTheTop() => Assert.Equal(TabBarSide.Top, new AppPreferences().TabBar);
}
