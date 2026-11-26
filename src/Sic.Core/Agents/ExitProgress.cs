namespace Sic.Core.Agents;

/// <summary>What the quit dialog says while live agents are asked to exit, and once each has either exited or run out of time.</summary>
public static class ExitProgress
{
    /// <summary>
    /// The dialog's summary line. <paramref name="stubborn"/> counts sessions still running when their time ran out; they are
    /// stopped once the dialog closes.
    /// </summary>
    public static string Summary(int total, int exited, int stubborn)
    {
        if (total < 1) return "No sessions are running. Closing SicTerm…";
        exited = Math.Clamp(exited, 0, total);
        stubborn = Math.Clamp(stubborn, 0, total - exited);
        if (exited + stubborn < total)
            return exited + stubborn == 0
                ? total == 1 ? "Waiting for the session to exit…" : $"Waiting for {total} sessions to exit…"
                : $"{exited} of {total} exited · waiting for the rest…";
        if (stubborn == 0) return total == 1 ? "The session exited cleanly. Closing SicTerm…" : $"All {total} sessions exited cleanly. Closing SicTerm…";
        if (total == 1) return "The session did not exit in time and will be stopped. Closing SicTerm…";
        var stopped = $"{stubborn} did not exit in time and will be stopped";
        return exited == 0 ? stopped + ". Closing SicTerm…" : $"{exited} exited cleanly; {stopped}. Closing SicTerm…";
    }

    /// <summary>The word beside one session: null while it is still being asked, then whether it exited in time.</summary>
    public static string State(bool? exited) => exited switch
    {
        null => "Exiting…",
        true => "Exited",
        false => "Will be stopped"
    };
}
