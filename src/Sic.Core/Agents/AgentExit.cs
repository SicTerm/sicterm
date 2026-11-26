using System.Diagnostics;

namespace Sic.Core.Agents;

/// <summary>
/// How SIC asks an agent to end its own session, so it writes its history and releases any "open elsewhere" marker before
/// the console goes away. <see cref="ClearLine"/> empties the input line first: typed onto an unsent draft, the command
/// would be submitted to the model as part of it. Enter goes as its own keystroke after a pause, because Hermes and Codex
/// read an Enter that arrives with or right behind typed text as a pasted newline and leave the command sitting in the box.
/// </summary>
public sealed record AgentExit(string ClearLine, string Command)
{
    // Ctrl+E then Ctrl+U: end of line, then delete to its start. Escape would interrupt Claude Code or Codex mid-turn.
    private const string EmacsClear = "\x05\x15";
    // PSReadLine's default Windows edit mode binds Escape to RevertLine and leaves Ctrl+E and Ctrl+U unbound.
    private const string PowerShellClear = "\x1b";

    /// <summary>Between the clear and the command.</summary>
    public static readonly TimeSpan ClearPause = TimeSpan.FromMilliseconds(200);
    /// <summary>Between the command and Enter: ten times Hermes's 50 ms pasted-newline window, the gap its own PTY tests use.</summary>
    public static readonly TimeSpan EnterPause = TimeSpan.FromMilliseconds(500);
    /// <summary>How long after Enter to press it once more, for an Enter that still landed as a newline under load.</summary>
    public static readonly TimeSpan EnterRetry = TimeSpan.FromSeconds(2);

    /// <summary>Null when the agent has no known exit command.</summary>
    public static AgentExit? For(string agentId) => agentId switch
    {
        "hermes" => new(EmacsClear, "/exit"),
        "claude" => new(EmacsClear, "/exit"),
        "codex" => new(EmacsClear, "/quit"),
        "opencode" => new(EmacsClear, "/exit"),
        "shell" => new(PowerShellClear, "exit"),
        _ => null
    };

    /// <summary>
    /// Clears the line, types the command, presses Enter, and presses it once more if the agent is still running
    /// <see cref="EnterRetry"/> later. Every pause waits on the process, so an agent that ends early ends the sequence.
    /// True when it ended within the budget.
    /// </summary>
    public async Task<bool> SendAsync(Func<string, Task> write, Func<TimeSpan, Task<bool>> waitForExit, TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        TimeSpan Left() => budget > clock.Elapsed ? budget - clock.Elapsed : TimeSpan.Zero;
        await write(ClearLine);
        if (await waitForExit(ClearPause)) return true;
        await write(Command);
        if (await waitForExit(EnterPause)) return true;
        await write("\r");
        if (await waitForExit(Left() < EnterRetry ? Left() : EnterRetry)) return true;
        if (Left() == TimeSpan.Zero) return false;
        await write("\r");
        return await waitForExit(Left());
    }
}
