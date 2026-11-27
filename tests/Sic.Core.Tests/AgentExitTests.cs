using System.Text;
using Sic.Core.Agents;
using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AgentExitTests
{
    private static string PowerShell => TestShells.PowerShell;

    [Theory]
    [InlineData("hermes", "\x05\x15", "/exit")]
    [InlineData("claude", "\x05\x15", "/exit")]
    [InlineData("codex", "\x05\x15", "/quit")]
    [InlineData("opencode", "\x05\x15", "/exit")]
    [InlineData("shell", "\x1b", "exit")]
    public void KnownAgentsClearTheLineThenTypeTheirOwnExitCommand(string agent, string clear, string command)
    {
        Assert.Equal(new AgentExit(clear, command), AgentExit.For(agent));
    }

    [Fact]
    public void UnknownAgentsHaveNoExitCommand()
    {
        Assert.Null(AgentExit.For("unknown"));
    }

    [Fact]
    public async Task SendAsync_PressesEnterAsItsOwnKeystrokeAfterTheCommand()
    {
        var terminal = new FakeTerminal(exitAfterEnters: 1);
        Assert.True(await AgentExit.For("codex")!.SendAsync(terminal.WriteAsync, terminal.WaitForExitAsync, TimeSpan.FromSeconds(8)));
        Assert.Equal(["\x05\x15", "/quit", "\r"], terminal.Writes);
        Assert.Equal([AgentExit.ClearPause, AgentExit.EnterPause, AgentExit.EnterRetry], terminal.Waits);
    }

    [Fact]
    public async Task SendAsync_PressesEnterAgainWhenTheFirstOneDidNotEndTheAgent()
    {
        var terminal = new FakeTerminal(exitAfterEnters: 2);
        Assert.True(await AgentExit.For("hermes")!.SendAsync(terminal.WriteAsync, terminal.WaitForExitAsync, TimeSpan.FromSeconds(8)));
        Assert.Equal(["\x05\x15", "/exit", "\r", "\r"], terminal.Writes);
    }

    [Fact]
    public async Task SendAsync_StopsTypingOnceTheAgentHasEnded()
    {
        var terminal = new FakeTerminal(exitAfterEnters: 0);
        Assert.True(await AgentExit.For("claude")!.SendAsync(terminal.WriteAsync, terminal.WaitForExitAsync, TimeSpan.FromSeconds(8)));
        Assert.Equal(["\x05\x15"], terminal.Writes);
    }

    [Fact]
    public async Task SendAsync_ReportsAnAgentThatNeverEnds()
    {
        var terminal = new FakeTerminal(exitAfterEnters: int.MaxValue);
        Assert.False(await AgentExit.For("claude")!.SendAsync(terminal.WriteAsync, terminal.WaitForExitAsync, TimeSpan.FromSeconds(8)));
        Assert.Equal(2, terminal.Writes.Count(text => text == "\r"));
    }

    /// <summary>
    /// PSReadLine is kept away from the user's history: with it, a prediction from that history (once a failed run had saved
    /// "Write-Output DRAFTexit" there) was drawn after the typed W, and the console host now passes every frame through, so the
    /// check below found the prediction rather than a draft that ran. Up to October 6, 2026 the host painted only some frames,
    /// which is why this test failed in some runs and not others. Nothing this test types is saved to that history any more.
    /// </summary>
    [Fact]
    public async Task SendAsync_EndsARealPowerShellWithoutRunningItsUnsentDraft()
    {
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NoExit", "-Command", "Set-PSReadLineOption -PredictionSource None -HistorySaveStyle SaveNothing"], AppContext.BaseDirectory));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var prompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = CaptureAsync(session, prompt, timeout.Token);
        await prompt.Task.WaitAsync(timeout.Token);
        await session.WriteInputAsync("Write-Output DRAFT", timeout.Token);
        async Task<bool> WaitForExitAsync(TimeSpan budget)
        {
            try { await session.Completion.WaitAsync(budget, timeout.Token); return true; }
            catch (TimeoutException) { return false; }
        }
        Assert.True(await AgentExit.For("shell")!.SendAsync(text => session.WriteInputAsync(text, timeout.Token), WaitForExitAsync, TimeSpan.FromSeconds(8)));
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        Assert.DoesNotContain("DRAFTexit", await capture);
    }

    private static async Task<string> CaptureAsync(ConPtySession session, TaskCompletionSource prompt, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await foreach (var bytes in session.Output.ReadAllAsync(cancellationToken))
        {
            text.Append(Encoding.UTF8.GetString(bytes));
            if (text.ToString().Contains("PS ")) prompt.TrySetResult();
        }
        return text.ToString();
    }

    /// <summary>A terminal whose process ends once it has seen the given number of Enter presses.</summary>
    private sealed class FakeTerminal(int exitAfterEnters)
    {
        internal List<string> Writes { get; } = [];
        internal List<TimeSpan> Waits { get; } = [];

        internal Task WriteAsync(string text) { Writes.Add(text); return Task.CompletedTask; }

        internal Task<bool> WaitForExitAsync(TimeSpan budget)
        {
            Waits.Add(budget);
            return Task.FromResult(Writes.Count(text => text == "\r") >= exitAfterEnters);
        }
    }
}
