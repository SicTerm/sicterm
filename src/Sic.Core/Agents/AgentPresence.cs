namespace Sic.Core.Agents;

/// <summary>
/// What a pane's agent is doing, as its tab shows it, in rising order of urgency, so a tab of several panes shows the most
/// urgent (docs/decisions/agent-presence.md, docs/decisions/program-status.md).
/// </summary>
public enum PaneState
{
    Idle,
    Working,
    /// <summary>The agent reported a finished turn whose result has not been seen (OSC 7501 <c>done</c>); only ever out of view.</summary>
    Done,
    /// <summary>The agent reported that it failed and stopped (OSC 7501 <c>error</c>); kept after the process ends, until seen.</summary>
    Error,
    /// <summary>A turn ended, a bell rang or the process ended out of view: the user is wanted, for a result or a question, and the agent did not say which.</summary>
    Attention,
    /// <summary>The agent reported that it cannot go on until the user acts (OSC 7501 <c>blocked</c>); shown in view too, until the agent reports otherwise.</summary>
    Blocked
}

/// <summary>
/// The window titles the agents set that say whether they are working, as recorded on September 28, 2026 with Claude Code
/// 2.1.284, Codex 0.157.1 and Hermes 0.21.5 (docs/decisions/agent-presence.md): a status glyph, a space, then a topic or
/// folder. Anything else says nothing, and <see cref="PresenceTracker"/> falls back to output. An agent that reports its own
/// status over OSC 7501 is not judged by its titles at all (docs/decisions/program-status.md); Claude Code leaves the glyph off
/// once a terminal has answered its query.
/// </summary>
public static class AgentTitles
{
    /// <summary>True for a working title, false for an idle one, null when the title says neither.</summary>
    public static bool? Working(string agentId, string title)
    {
        if (title.Length < 2 || title[1] != ' ') return null;
        var glyph = title[0];
        return agentId switch
        {
            // "◐ Topic" and "◑ Topic" alternate about once a second while working; ◒ and ◓, the rest of that sequence, were not
            // seen but are taken as working too. "✳ Topic" when idle, and also while a permission prompt waits.
            "claude" => glyph is >= '◐' and <= '◓' ? true : glyph == '✳' ? false : null,
            // "⠋ | folder" while working, but only for a session's first turn; later turns keep the idle title throughout, so a
            // Codex title can say working but never idle, and the rest is left to output.
            "codex" => glyph is >= '⠀' and <= '⣿' ? true : null,
            // "⏳ name · model · folder" while working, "✓ …" when ready.
            "hermes" => glyph == '⏳' ? true : glyph == '✓' ? false : null,
            // "OpenCode", then "OC | Topic", working or not (OpenCode 2.0.23, October 5, 2026), so output decides: OpenCode is
            // silent at rest and redraws many times a second while it works.
            "opencode" => null,
            _ => null
        };
    }
}

/// <summary>
/// Decides whether a running pane is working, idle, or waiting for the user after a turn ended out of sight, from what its
/// terminal emits. The agent's own report wins (OSC 7501, <see cref="Report"/>): working, blocked, done, idle or error, as
/// Claude Code, Codex and OpenCode send them (docs/decisions/program-status.md). Without one, a title the agent is known to
/// set wins (<see cref="AgentTitles"/>): Hermes redraws its status bar every second while idle and Claude Code blinks a dot at
/// a permission prompt, so output would call both working. Without that, output in the last <see cref="QuietAfter"/> means
/// working, except output that only answers what the terminal sent it (<see cref="EchoWindow"/>). A turn that ends while the
/// pane is out of view (<see cref="Selected"/>), a bell there, or the process ending there, asks for attention until the pane is
/// in view, unless the agent said how the turn ended, in which case that is shown; work before the user has typed into the
/// pane, a launch or a resume, is not a turn and does not. It also counts the turns the user started that have ended out of
/// view (<see cref="TurnsEnded"/>), which ring the ready chime. Time is passed in, so transitions that happen by time alone are
/// found when the state is next asked for; the app asks twice a second.
/// </summary>
public sealed class PresenceTracker(string agentId, bool selected = true)
{
    /// <summary>
    /// How soon after the terminal sent the pane something its output counts as an answer to it rather than as work, while
    /// the pane is not working already. Codex and OpenCode redraw when their pane gains or loses the focus (xterm's focus
    /// reports, which both turn on), and both echo each key: measured on October 13, 2026 with Codex 0.162.1 and OpenCode, each
    /// answer was done within 50 ms. Judged by output, such a redraw read as a turn, and in a tab just left, as a turn that ended
    /// there. A real turn outlasts the window, so it shows as working that much later.
    /// </summary>
    public static readonly TimeSpan EchoWindow = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long a pane without a telling title stays working after its last output. Codex went quiet for up to 2.3 s between
    /// redraws in the middle of a turn, so a shorter window would end the turn early and flag it in a background tab.
    /// </summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long an agent must work on after the user's last keystroke for the rest that follows to count as a finished turn:
    /// long enough that the user may have looked away, so a quick reply read as it arrives stays quiet. It is also more than
    /// <see cref="QuietAfter"/>, which the redraws that echo typing into an agent judged by output (Codex after its first turn)
    /// can hold a pane working for. A turn the agent reports the end of needs no minimum.
    /// </summary>
    public static readonly TimeSpan MinimumTurn = TimeSpan.FromSeconds(5);

    private DateTimeOffset? _lastOutput, _lastInput, _lastSent;
    private bool? _titleWorking;
    // The root record of the agent's own status report (OSC 7501), while it has made one and not withdrawn it. While it stands
    // it alone says whether the agent works; titles and output are still recorded but not consulted.
    private ProgramState? _reported;
    private bool _working, _attention, _ended, _inputSinceRest, _typed;

    public bool IsSelected { get; private set; } = selected;

    /// <summary>Whether the agent reports its own status (OSC 7501), so that titles and output are not consulted.</summary>
    public bool Reporting => _reported is not null;

    /// <summary>The state the program last reported over OSC 7501, or null when it has reported none (or withdrew it).</summary>
    public ProgramState? Reported => _reported;

    /// <summary>With a reported <see cref="PaneState.Blocked"/>, what the agent waits for: "permission", "question" or "auth"; null when it did not say.</summary>
    public string? BlockedKind { get; private set; }

    /// <summary>
    /// Turns the user started that have ended out of view: the first rest after the user typed in the pane, once the agent has
    /// worked on for at least <see cref="MinimumTurn"/> after the last keystroke. A turn that ends while the pane is in view is
    /// being watched, and does not count. A launch or a resume, which nobody typed into, never counts,
    /// nor does a rest reached by the process ending, nor a PowerShell pane, whose commands are not turns. A finished turn and a
    /// question the agent stops to ask, such as a permission prompt, count alike, as they look alike (<see cref="AgentTitles"/>).
    /// An agent that reports its own status ends a turn by saying so: done, error or blocked out of view counts at once, with no
    /// minimum, and blocked counts even when nothing was typed, since a prompt as the agent starts (folder trust, a sign-in)
    /// wants the user as much as one mid-turn.
    /// </summary>
    public int TurnsEnded { get; private set; }

    /// <summary>
    /// Whether data the terminal sends to the pane's process came from the user. xterm answers the agent's own queries (device
    /// attributes, cursor position, colors, whether the terminal reads status reports) and reports focus changes on the same
    /// channel as keystrokes, and every such answer begins with ESC; so do the arrow keys, Escape and mouse reports, which is no
    /// loss, since a turn is started by typed text or Enter.
    /// </summary>
    public static bool IsUserInput(string data) => data.Length > 0 && data[0] != '\u001b';

    /// <summary>
    /// The pane showed output, the latest <paramref name="age"/> ago: the page reports a burst at its start and once at its end,
    /// so the end's report comes later than the output it reports.
    /// </summary>
    public void Output(DateTimeOffset now, TimeSpan age = default)
    {
        Update(now);
        var at = now - age;
        // An answer to what the terminal just sent (a redraw for a focus change, a key's echo) is not work by itself.
        if (!_working && _lastSent is { } sent && at >= sent && at - sent < EchoWindow) return;
        if (_lastOutput is not { } last || at > last) _lastOutput = at;
        Update(now);
    }

    /// <summary>The user typed or pasted into the pane (see <see cref="IsUserInput"/>).</summary>
    public void Input(DateTimeOffset now) { Update(now); _lastInput = _lastSent = now; _inputSinceRest = _typed = true; }

    /// <summary>The terminal sent the pane's process something that is not the user's typing: a focus or mouse report, an arrow key, an answer to a query.</summary>
    public void Sent(DateTimeOffset now) { Update(now); _lastSent = now; }

    public void Title(string title, DateTimeOffset now) { Update(now); _titleWorking = AgentTitles.Working(agentId, title); Update(now); }

    public void Bell(DateTimeOffset now)
    {
        Update(now);
        if (!IsSelected && !_ended) _attention = true;
    }

    /// <summary>
    /// The agent reported its state over OSC 7501 (docs/decisions/program-status.md). Only the root record speaks for the pane;
    /// a sub-record ("build/test") is the agent's own bookkeeping. A result (done, error) that arrives in view is seen as it
    /// comes, and kept as idle; out of view it is shown until the pane comes into view. Blocked is shown in view as well, and
    /// stays until the agent reports otherwise. Clear withdraws the report, and titles and output decide again.
    /// </summary>
    public void Report(ProgramStatus status, DateTimeOffset now)
    {
        Update(now);
        if (status.Id is not null) return;
        var previous = _reported;
        if (status.State == ProgramState.Clear)
        {
            _reported = null;
            BlockedKind = null;
            Update(now);
            return;
        }
        _reported = status.State is ProgramState.Done or ProgramState.Error && IsSelected ? ProgramState.Idle : status.State;
        BlockedKind = _reported == ProgramState.Blocked ? status.Kind : null;
        var turns = TurnsEnded;
        Update(now);
        // Blocked with no turn's end before it (a trust or sign-in prompt as the agent starts) wants the user just the same.
        if (_reported == ProgramState.Blocked && previous != ProgramState.Blocked && TurnsEnded == turns && !IsSelected && !_ended && agentId != "shell") TurnsEnded++;
    }

    /// <summary>
    /// Whether the pane is in view: its tab is the selected one, in a window that is shown and active, so the user is watching
    /// it. Coming into view clears attention, and a reported result is seen.
    /// </summary>
    public void Selected(bool selected, DateTimeOffset now)
    {
        Update(now);
        IsSelected = selected;
        if (!selected) return;
        _attention = false;
        if (_reported is ProgramState.Done or ProgramState.Error) _reported = ProgramState.Idle;
    }

    public void Exited(DateTimeOffset now)
    {
        Update(now);
        // The protocol's rule: working and blocked end with the process; done and error outlive it, so a result nobody saw still shows.
        if (_reported is ProgramState.Working or ProgramState.Blocked) { _reported = ProgramState.Idle; BlockedKind = null; }
        if (!_ended && !IsSelected && _reported is not (ProgramState.Done or ProgramState.Error)) _attention = true;
        _ended = true;
        Update(now);
    }

    public PaneState State(DateTimeOffset now)
    {
        Update(now);
        if (_reported == ProgramState.Blocked) return PaneState.Blocked;
        if (_attention) return PaneState.Attention;
        if (_reported == ProgramState.Error) return PaneState.Error;
        if (_reported == ProgramState.Done) return PaneState.Done;
        return _working ? PaneState.Working : PaneState.Idle;
    }

    private void Update(DateTimeOffset now)
    {
        var working = !_ended && (_reported is { } reported ? reported == ProgramState.Working : _titleWorking ?? (_lastOutput is { } last && now - last < QuietAfter));
        if (_working && !working)
        {
            // The agent said how the turn ended: that state is shown in attention's place.
            var said = _reported is ProgramState.Done or ProgramState.Blocked or ProgramState.Error;
            // Until the user has typed into the pane, its work is a launch or a resume drawing itself and the conversation it
            // brought back, which nobody is waiting for: a pane resumed in a background tab settles to running, not Your turn.
            if (!IsSelected && _typed && !said) _attention = true;
            // Judged by output, the work ended with the last output, not when the quiet window ran out or was next asked about.
            var rested = _reported is null && _titleWorking is null && _lastOutput is { } output ? output : now;
            var turn = said
                ? _reported == ProgramState.Blocked || _inputSinceRest
                : _inputSinceRest && _lastInput is { } input && rested - input >= MinimumTurn;
            if (turn && !_ended && !IsSelected && agentId != "shell") TurnsEnded++;
            _inputSinceRest = false;
        }
        _working = working;
    }
}
