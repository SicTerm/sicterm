namespace Sic.Core.Sessions;

/// <summary>
/// New sessions known only by the folder they run in and the time they began, as a new Hermes session is since SicTerm
/// stopped naming it (docs/decisions/hermes-session-names.md), start one at a time per agent history and folder. A start
/// waits until the one before it in the same folder has been linked, or for <see cref="Wait"/> at most, and a session found
/// for a turn must have begun before the next turn's launch, so two new sessions in one folder are never taken for each other.
/// Not thread-safe: SicTerm uses it from the UI thread only.
/// </summary>
public sealed class CreationTurns
{
    /// <summary>The longest a start waits for the one before it: Hermes writes a new session's row 3 to 10 seconds after launch.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly List<CreationTurn> _turns = [];

    /// <summary>The key of a new session in <paramref name="folder"/> of the agent whose history is <paramref name="historyDirectory"/>.</summary>
    public static string Key(string historyDirectory, string folder) => FolderIdentity.Normalize(historyDirectory) + "|" + FolderIdentity.Normalize(folder);

    /// <summary>Whether a turn in <paramref name="key"/> is under way, which a new start there would wait for.</summary>
    public bool Busy(string key) => _turns.Any(turn => Same(turn.Key, key));

    /// <summary>
    /// A turn in <paramref name="key"/>, once the last turn taken there before it has ended or <paramref name="wait"/> has passed.
    /// The turn is held from the call, so a start asked for meanwhile waits for this one in turn.
    /// </summary>
    public async Task<CreationTurn> TakeAsync(string key, TimeSpan wait, CancellationToken cancellation)
    {
        var before = _turns.LastOrDefault(turn => Same(turn.Key, key));
        var turn = new CreationTurn(key);
        _turns.Add(turn);
        try
        {
            if (before is not null) await Task.WhenAny(before.Ended, Task.Delay(wait, cancellation));
            cancellation.ThrowIfCancellationRequested();
            return turn;
        }
        catch { End(turn); throw; }
    }

    /// <summary>Records the turn's launch, which closes the window of each earlier turn in its key that is still looking.</summary>
    public void Launched(CreationTurn turn, DateTimeOffset at)
    {
        turn.LaunchedAt = at;
        foreach (var earlier in _turns)
            if (!ReferenceEquals(earlier, turn) && Same(earlier.Key, turn.Key) && earlier.LaunchedAt <= at) earlier.Until ??= at;
    }

    /// <summary>Ends the turn: its session was linked, its pane stopped, or it was never launched.</summary>
    public void End(CreationTurn turn)
    {
        _turns.Remove(turn);
        turn.End();
    }

    private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One new session's turn (<see cref="CreationTurns"/>).</summary>
public sealed class CreationTurn
{
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CreationTurn(string key) => Key = key;

    public string Key { get; }

    /// <summary>When the session was launched; null until it is.</summary>
    public DateTimeOffset? LaunchedAt { get; internal set; }

    /// <summary>The launch of the next turn in the same key, which this turn's session must have begun before; null while there is none.</summary>
    public DateTimeOffset? Until { get; internal set; }

    internal Task Ended => _ended.Task;

    internal void End() => _ended.TrySetResult();
}
