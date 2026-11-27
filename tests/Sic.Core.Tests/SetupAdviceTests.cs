using System.Text.Json;
using Sic.Core.Shell;
using Sic.Core.State;
using Xunit;

namespace Sic.Core.Tests;

public sealed class SetupAdviceTests
{
    [Theory]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", true)]
    [InlineData(@"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\pwsh.exe", true)]
    [InlineData(@"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\PWSH.EXE", true)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPowerShell7_IsPwshWhereverItWasFound(string? shell, bool expected) => Assert.Equal(expected, SetupAdvisor.IsPowerShell7(shell));

    [Theory]
    [InlineData(true, true, SetupGap.None)]
    [InlineData(false, true, SetupGap.PowerShell)]
    [InlineData(true, false, SetupGap.Agent)]
    [InlineData(false, false, SetupGap.Both)]
    // Detection has not answered, or failed: no agent gap is claimed.
    [InlineData(true, null, SetupGap.None)]
    [InlineData(false, null, SetupGap.PowerShell)]
    public void Gaps_ClaimsNoMissingAgentUntilDetectionHasSaidSo(bool powerShell7, bool? agentFound, SetupGap expected)
        => Assert.Equal(expected, SetupAdvisor.Gaps(powerShell7, agentFound));

    [Theory]
    [InlineData(SetupGap.Both, SetupGap.None, SetupGap.Both)]
    [InlineData(SetupGap.Both, SetupGap.PowerShell, SetupGap.Agent)]
    [InlineData(SetupGap.Both, SetupGap.Both, SetupGap.None)]
    // Closing the combined notice closes each half: a gap that comes later alone stays closed.
    [InlineData(SetupGap.Agent, SetupGap.Both, SetupGap.None)]
    [InlineData(SetupGap.None, SetupGap.None, SetupGap.None)]
    public void Open_LeavesOutWhatWasClosedInThatPlace(SetupGap gaps, SetupGap closed, SetupGap expected)
        => Assert.Equal(expected, SetupAdvisor.Open(gaps, closed));

    [Fact]
    public void For_SaysEachGapWithItsInstallPages()
    {
        Assert.Null(SetupAdvisor.For(SetupGap.None));
        var powerShell = SetupAdvisor.For(SetupGap.PowerShell)!;
        Assert.Equal("PowerShell 7 was not found, so SicTerm runs in Windows PowerShell 5.1. SicTerm works in 5.1, but PowerShell 7 or later is recommended.", powerShell.Message);
        Assert.Equal(["PowerShell 7"], powerShell.Links.Select(link => link.Name));
        var agent = SetupAdvisor.For(SetupGap.Agent)!;
        Assert.Equal("No coding agent was found. SicTerm works as a terminal on its own, but it is built around coding agents, and installing one is recommended.", agent.Message);
        Assert.Equal(["Claude Code", "Codex", "OpenCode", "Hermes"], agent.Links.Select(link => link.Name));
        var both = SetupAdvisor.For(SetupGap.Both)!;
        Assert.Equal("PowerShell 7 and a coding agent were not found. SicTerm works as a terminal in Windows PowerShell 5.1. PowerShell 7 and a coding agent are recommended.", both.Message);
        Assert.Equal(["PowerShell 7", "Claude Code", "Codex", "OpenCode", "Hermes"], both.Links.Select(link => link.Name));
        // Each link is a vendor's own page, over HTTPS; none is a Store page.
        Assert.All(both.Links, link => Assert.Equal(Uri.UriSchemeHttps, link.Uri.Scheme));
        Assert.DoesNotContain(both.Links, link => link.Uri.Host.Contains("microsoft.com", StringComparison.Ordinal) && link.Uri.Host.StartsWith("apps.", StringComparison.Ordinal));
    }

    [Fact]
    public void ClosedGaps_AreKeptInTheSettingsFile()
    {
        var preferences = new AppPreferences { SetupClosedAtLaunch = SetupGap.Both, SetupClosedInSettings = SetupGap.Agent };
        var read = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!;
        Assert.Equal(SetupGap.Both, read.SetupClosedAtLaunch);
        Assert.Equal(SetupGap.Agent, read.SetupClosedInSettings);
        Assert.Equal(SetupGap.None, new AppPreferences().SetupClosedAtLaunch);
    }
}
