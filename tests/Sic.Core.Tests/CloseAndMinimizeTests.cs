using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class CloseAndMinimizeTests
{
    [Theory]
    [InlineData("tray", "tray")]
    [InlineData("close-to-tray", "close-to-tray")]
    [InlineData(" QUIT ", "quit")]
    [InlineData(null, "tray")]
    [InlineData("", "tray")]
    [InlineData("minimize", "tray")]
    public void AnUnknownWordSendsBothToTheTray(string? choice, string expected) => Assert.Equal(expected, CloseAndMinimize.Normalize(choice));

    [Theory]
    [InlineData("tray", true, true)]
    [InlineData("close-to-tray", true, false)]
    [InlineData("quit", false, false)]
    [InlineData("unknown", true, true)]
    public void EachChoiceSaysWhereCloseAndMinimizeGo(string choice, bool closesToTray, bool minimizesToTray)
    {
        Assert.Equal(closesToTray, CloseAndMinimize.ClosesToTray(choice));
        Assert.Equal(minimizesToTray, CloseAndMinimize.MinimizesToTray(choice));
    }

    [Fact]
    public void TheDefaultIsBothToTheTrayAndEveryChoiceHasALabelAndNormalizesToItself()
    {
        Assert.Equal(CloseAndMinimize.Tray, new Sic.Core.State.AppPreferences().CloseAndMinimize);
        Assert.Equal(CloseAndMinimize.Tray, CloseAndMinimize.Choices[0]);
        foreach (var choice in CloseAndMinimize.Choices)
        {
            Assert.Equal(choice, CloseAndMinimize.Normalize(choice));
            Assert.NotEmpty(CloseAndMinimize.Label(choice));
        }
        Assert.Equal("Close and minimize to tray", CloseAndMinimize.Label(null));
        Assert.Equal(3, CloseAndMinimize.Choices.Distinct().Count());
    }
}
