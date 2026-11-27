using System.Text;
using System.Threading.Channels;
using Sic.Core.Agents;
using Sic.Core.Shell;
using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ShellStatusTests(Xunit.Abstractions.ITestOutputHelper testOutput)
{
    [Fact]
    public void Reports_AreOnesTheTrackerReads()
    {
        Assert.Equal(ProgramState.Working, ProgramStatus.Parse(ShellStatus.Working[7..^1])!.State);
        Assert.Equal(ProgramState.Idle, ProgramStatus.Parse(ShellStatus.Idle[7..^1])!.State);
    }

    [Fact]
    public void Arguments_PutTheCardsOwnBeforeTheHook()
    {
        var arguments = ShellStatus.Arguments(["-NoProfile"]);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand"], arguments.Take(4));
        Assert.Equal(ShellStatus.Script, Encoding.Unicode.GetString(Convert.FromBase64String(arguments[4])));
    }

    /// <summary>
    /// A real shell, in both PowerShells: idle at the first prompt, working once Enter sends a command, and idle again when the
    /// prompt is back, with the command's output in between and its failure still seen by the prompt.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AShellTab_SaysWorkingWhileACommandRunsAndIdleAtThePrompt(bool windowsPowerShell)
    {
        var shell = windowsPowerShell ? PowerShellLocator.WindowsPowerShell : TestShells.PowerShell;
        // Windows PowerShell 5.1 does not load its own modules, PSReadLine among them, with PowerShell 7's PSModulePath.
        var environment = new Dictionary<string, string?> { ["PSModulePath"] = null };
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(shell, ShellStatus.Arguments(["-NoProfile"]), AppContext.BaseDirectory, Environment: environment));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var text = new StringBuilder();
        async Task WaitForAsync(Func<string, bool> done)
        {
            while (!done(text.ToString()))
            {
                var chunk = await session.Output.ReadAsync(timeout.Token);
                text.Append(Encoding.UTF8.GetString(chunk));
            }
        }
        try
        {
            await WaitForAsync(output => output.Contains(ShellStatus.Idle, StringComparison.Ordinal));
            var first = text.Length;
            await session.WriteInputAsync("Start-Sleep -Milliseconds 400; 'SLEPT'; Get-Item C:\\nothing-here-at-all\r", timeout.Token);
            // The typed line is echoed first, with SLEPT in it; the command's own output comes after working.
            int Working(string output) => output.IndexOf(ShellStatus.Working, first, StringComparison.Ordinal);
            int Printed(string output) => Working(output) is var at and >= 0 ? output.IndexOf("SLEPT", at, StringComparison.Ordinal) : -1;
            await WaitForAsync(output => Printed(output) is var slept and >= 0 && output.IndexOf(ShellStatus.Idle, slept, StringComparison.Ordinal) >= 0);
            var all = text.ToString();
            var working = Working(all);
            var printed = Printed(all);
            var idleAgain = all.IndexOf(ShellStatus.Idle, printed, StringComparison.Ordinal);
            Assert.DoesNotContain(ShellStatus.Idle, all[first..working]);
            Assert.True(working >= 0 && working < printed && printed < idleAgain, $"working {working}, output {printed}, idle {idleAgain}");
        }
        finally { testOutput.WriteLine(System.Text.Json.JsonSerializer.Serialize(text.ToString())); }
    }
}
