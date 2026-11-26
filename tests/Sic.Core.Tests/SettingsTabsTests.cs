using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class SettingsTabsTests
{
    [Theory]
    [InlineData("agents", "agents")]
    [InlineData(" Terminal ", "terminal")]
    [InlineData("about", "about")]
    [InlineData("Appearance", "appearance")]
    [InlineData(null, "general")]
    [InlineData("", "general")]
    [InlineData("keyboard", "general")]
    public void AnUnknownWordOpensGeneral(string? tab, string expected) => Assert.Equal(expected, SettingsTabs.Normalize(tab));

    [Fact]
    public void FiveTabsEachWithALabelThatNormalizeToThemselves()
    {
        Assert.Equal(5, SettingsTabs.Choices.Count);
        foreach (var choice in SettingsTabs.Choices)
        {
            Assert.Equal(choice, SettingsTabs.Normalize(choice));
            Assert.NotEmpty(SettingsTabs.Label(choice));
        }
        Assert.Equal("General", SettingsTabs.Label("sideways"));
    }
}
