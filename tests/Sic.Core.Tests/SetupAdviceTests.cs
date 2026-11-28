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
    public void For_SaysEachGapInOneSentenceOrTwo()
    {
        Assert.Null(SetupAdvisor.For(SetupGap.None));
        Assert.Equal("PowerShell 7 was not found. SicTerm is running in Windows PowerShell 5.1, where some coding tools and commands fail.", SetupAdvisor.For(SetupGap.PowerShell)!.Message);
        Assert.Equal("No coding agent was found.", SetupAdvisor.For(SetupGap.Agent)!.Message);
        Assert.Equal("PowerShell 7 and a coding agent were not found.", SetupAdvisor.For(SetupGap.Both)!.Message);
    }

    [Fact]
    public void ClosedGaps_AreKeptInTheSettingsFile()
    {
        var preferences = new AppPreferences { SetupClosedAtLaunch = SetupGap.Both, SetupClosedOnHome = SetupGap.Agent };
        var read = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!;
        Assert.Equal(SetupGap.Both, read.SetupClosedAtLaunch);
        Assert.Equal(SetupGap.Agent, read.SetupClosedOnHome);
        Assert.Equal(SetupGap.None, new AppPreferences().SetupClosedAtLaunch);
    }

    [Fact]
    public void Items_ArePowerShell7ThenTheFourAgentsEachWithItsVendorsPage()
    {
        Assert.Equal(["PowerShell 7", "Claude Code", "Codex", "Hermes", "OpenCode"], SetupCatalog.Items.Select(item => item.Name));
        Assert.Equal(["powershell", "claude", "codex", "hermes", "opencode"], SetupCatalog.Items.Select(item => item.Id));
        // Each page is a vendor's own, over HTTPS; none is a Store page.
        Assert.All(SetupCatalog.Items, item => Assert.Equal(Uri.UriSchemeHttps, item.Page.Scheme));
        Assert.DoesNotContain(SetupCatalog.Items, item => item.Page.Host.StartsWith("apps.", StringComparison.Ordinal));
        // The wording states facts: it never addresses the reader.
        Assert.All(SetupCatalog.Items, item => Assert.DoesNotMatch(@"\b[Yy]ou(r)?\b", item.Line + " " + item.Detail));
        Assert.DoesNotMatch(@"\b[Yy]ou(r)?\b", SetupCatalog.AgentsLabel + SetupCatalog.Footnote + SetupCatalog.ClaudeOffPath);
    }

    [Theory]
    [InlineData("powershell", true, false, "winget install --id Microsoft.PowerShell --source winget")]
    // No winget (Windows Sandbox, Server): the row offers the install page.
    [InlineData("powershell", false, true, null)]
    [InlineData("claude", false, false, "irm https://claude.ai/install.ps1 | iex")]
    [InlineData("codex", false, false, "powershell -ExecutionPolicy ByPass -c \"irm https://chatgpt.com/codex/install.ps1 | iex\"")]
    [InlineData("hermes", false, false, "iex (irm https://hermes-agent.nousresearch.com/install.ps1)")]
    // OpenCode has no Windows installer: npm where Node.js is installed, by its .cmd, which a Restricted policy does not refuse.
    [InlineData("opencode", false, true, "npm.cmd install -g --allow-scripts=@opencode/cli @opencode/cli@latest")]
    [InlineData("opencode", true, false, null)]
    [InlineData("something else", true, true, null)]
    public void Command_IsTheVendorsOwnWhereThisPcCanRunOne(string id, bool winget, bool npm, string? expected)
        => Assert.Equal(expected, SetupCatalog.Command(id, winget, npm));

    [Fact]
    public void Command_IsOneLineThatNeverRunsItself()
    {
        foreach (var item in SetupCatalog.Items)
        {
            var command = SetupCatalog.Command(item.Id, winget: true, npm: true)!;
            Assert.DoesNotContain('\n', command);
            Assert.DoesNotContain('\r', command);
            Assert.Equal(command.Trim(), command);
        }
    }

    [Fact]
    public void KnownProgram_IsWhereTheQuietInstallersPutTheirs()
    {
        Assert.Equal(@"C:\Users\someone\.local\bin\claude.exe", SetupCatalog.KnownProgram("claude", @"C:\Users\someone", @"C:\Users\someone\AppData\Local"));
        Assert.Equal(@"C:\Users\someone\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe", SetupCatalog.KnownProgram("codex", @"C:\Users\someone", @"C:\Users\someone\AppData\Local"));
        Assert.Null(SetupCatalog.KnownProgram("hermes", @"C:\Users\someone", @"C:\Users\someone\AppData\Local"));
        Assert.Equal(@"C:\Users\someone\.local\bin", SetupCatalog.ClaudeFolder(@"C:\Users\someone"));
    }
}
