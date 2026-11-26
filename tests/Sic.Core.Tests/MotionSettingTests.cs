using Sic.Core.Appearance;
using Xunit;

namespace Sic.Core.Tests;

public sealed class MotionSettingTests
{
    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData(" Off ", false)]
    [InlineData("always", true)]
    [InlineData("never", false)]
    [InlineData("system", true)]
    [InlineData(null, true)]
    [InlineData("sometimes", true)]
    public void OnUnlessOff(string? setting, bool expected) => Assert.Equal(expected, MotionSetting.Resolve(setting));

    [Theory]
    [InlineData(null, "on")]
    [InlineData("ALWAYS", "on")]
    [InlineData("system", "on")]
    [InlineData("false", "off")]
    [InlineData("never", "off")]
    [InlineData("whatever", "on")]
    public void EarlierWordsMapToOnAndOff(string? setting, string expected) => Assert.Equal(expected, MotionSetting.Normalize(setting));

    [Fact]
    public void EveryChoiceHasALabelAndNormalizesToItself()
    {
        foreach (var choice in MotionSetting.Choices)
        {
            Assert.Equal(choice, MotionSetting.Normalize(choice));
            Assert.NotEmpty(MotionSetting.Label(choice));
        }
        Assert.Equal(["On", "Off"], MotionSetting.Choices.Select(MotionSetting.Label));
    }
}
