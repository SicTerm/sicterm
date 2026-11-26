namespace Sic.Core.Agents;

/// <summary>
/// One run of an operation at a time: a caller that arrives while a run is under way gets that run's result instead of
/// starting another. A Quit or a Windows logoff while a tab or pane is closing, or a tab closed while one of its panes is
/// closing, would otherwise type an agent's exit command into its console a second time. Once a run ends the next call
/// starts a fresh one. Not thread-safe: SIC calls it from the UI thread only.
/// </summary>
public sealed class SingleFlight<T>
{
    private Task<T>? _current;

    /// <summary>Whether a run is under way.</summary>
    public bool Running => _current is not null;

    /// <summary>The run under way, null when there is none: for a caller that shows the run's progress without joining it.</summary>
    public Task<T>? Current => _current;

    public async Task<T> RunAsync(Func<Task<T>> operation)
    {
        if (_current is { } underway) return await underway;
        var run = operation();
        _current = run;
        // Cleared after the run is recorded, so one that completed synchronously is not kept as the answer for later calls.
        try { return await run; }
        finally { if (ReferenceEquals(_current, run)) _current = null; }
    }
}
