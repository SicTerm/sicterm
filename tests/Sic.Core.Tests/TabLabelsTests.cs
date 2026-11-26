using Sic.Core.Appearance;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TabLabelsTests
{
    [Theory]
    [InlineData("Claude Code · web.cloud.portal.docs", "web.cloud.portal.docs")]
    [InlineData("Hermes · DEVBOX01", "DEVBOX01")]
    [InlineData("Codex · super-intelligent-chat", "super-intelligent-chat")]
    [InlineData("OpenCode · super-intelligent-chat", "super-intelligent-chat")]
    [InlineData("PowerShell · integration check", "integration check")]
    [InlineData("Untitled Hermes session", "Untitled session")]
    public void DropsTheHarnessNameTheTileAlreadyShows(string title, string expected) =>
        Assert.Equal(expected, TabLabels.WithoutAgentName(title));

    [Theory]
    [InlineData("Fixture · terminal integration")]
    [InlineData("Blue Maple migration")]
    [InlineData("Fix the Codex · resume bug")]
    [InlineData("claude code · lowercase is someone's own name")]
    [InlineData("Claude Code")]
    [InlineData("Claude Code · ")]
    public void LeavesOtherTitlesAndNeverEmptiesOne(string title) =>
        Assert.Equal(title, TabLabels.WithoutAgentName(title));

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal) { ["claude:s1"] = "Blue Maple migration" };

    [Fact]
    public void APaneIsCalledByItsConversationsLocalNameThenItsOwnThenItsTitle()
    {
        var linked = new SavedPane("p", "claude", "Claude Code · repo", @"C:\repo", "", "s1");
        Assert.Equal("Blue Maple migration", TabLabels.PaneTitle(linked, Names));
        Assert.Equal("Claude Code · repo", TabLabels.PaneTitle(linked with { SessionId = "s2" }, Names));
        Assert.Equal("Left side", TabLabels.PaneTitle(linked with { SessionId = "s2", CustomTitle = "Left side" }, Names));
        // The local name stands above a name given to the pane before it was linked.
        Assert.Equal("Blue Maple migration", TabLabels.PaneTitle(linked with { CustomTitle = "Left side" }, Names));
        var shell = new SavedPane("p", "shell", "PowerShell · repo", @"C:\repo", "", CustomTitle: "Build watch");
        Assert.Equal("Build watch", TabLabels.PaneTitle(shell, Names));
        Assert.Equal("Untitled Hermes session", TabLabels.PaneTitle(new SavedPane("p", "hermes", "SIC-0123456789abcdef0123456789abcdef", @"C:\repo", ""), Names));
    }

    [Fact]
    public void ATabIsCalledByItsOwnNameElseItsFirstPanes()
    {
        var first = new SavedPane("a", "claude", "Claude Code · repo", @"C:\repo", "", "s1");
        var second = new SavedPane("b", "shell", "PowerShell · repo", @"C:\repo", "", CustomTitle: "Build watch");
        Assert.Equal("Blue Maple migration", TabLabels.TabTitle(new SavedTab("t", "Original", [first, second]), Names));
        Assert.Equal("Build watch", TabLabels.TabTitle(new SavedTab("t", "Original", [second, first]), Names));
        Assert.Equal("Repo work", TabLabels.TabTitle(new SavedTab("t", "Original", [first, second], CustomTitle: "Repo work"), Names));
        Assert.Equal("Original", TabLabels.TabTitle(new SavedTab("t", "Original", []), Names));
    }

    [Theory]
    [InlineData("Blue Maple", true)]
    [InlineData("  x  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("two\nlines", false)]
    public void ANameIsOneLineOfOneToOneHundredAndTwentyCharacters(string name, bool valid)
    {
        Assert.Equal(valid, TabLabels.IsValidName(name));
        Assert.True(TabLabels.IsValidName(new string('n', 120)));
        Assert.False(TabLabels.IsValidName(new string('n', 121)));
    }

    [Fact]
    public void KeysAndCreationTokensAreSpelledTheSameEverywhere()
    {
        Assert.Equal("claude:s1", TabLabels.Key("claude", "s1"));
        Assert.Equal("Untitled Hermes session", TabLabels.Display("CommandCentral-0123456789abcdef0123456789abcdef"));
        Assert.Equal("SIC-not-a-token", TabLabels.Display("SIC-not-a-token"));
    }
}
