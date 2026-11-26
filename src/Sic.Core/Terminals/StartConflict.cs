using System.Globalization;
using System.Text;

namespace Sic.Core.Terminals;

/// <summary>What asked a pane to start, in the words the notice uses ("a double-click on the conversation in Sessions"), and when.</summary>
public sealed record StartRequest(string Trigger, DateTimeOffset At);

/// <summary>
/// A request to start a pane that found another request had already started it. The notice says only that (<see cref="Summary"/>).
/// "This pane is already running." alone once came up often enough to mean nothing, so <see cref="Describe"/> adds, for the
/// diagnostics log, what tells the likely causes apart: which request came first and from where, whether the other is still
/// starting or already running and as which process, and how long this one spent in the history refresh and in the checks
/// of the saved history around it. A holder asked for later than this request overtook it. Resumes of one pane now run one
/// at a time (SessionPane.ResumeOnceAsync), so the notice is left for a start that reaches a pane some other way.
/// </summary>
public sealed record StartConflict
{
    /// <summary>The request that was refused.</summary>
    public required StartRequest Request { get; init; }
    /// <summary>The request whose terminal the pane holds.</summary>
    public required StartRequest Holder { get; init; }
    /// <summary>True when the holder's process is running; false while its terminal is still starting.</summary>
    public required bool Running { get; init; }
    public required string Agent { get; init; }
    /// <summary>The tab's title; null when the pane, or its tab, closed while the request waited.</summary>
    public string? Tab { get; init; }
    public int PaneNumber { get; init; } = 1;
    public int PaneCount { get; init; } = 1;
    public string? SessionId { get; init; }
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartedAt { get; init; }
    /// <summary>How long this request's history refresh took; null on a path without one, as a shell's or a resume whose saved history matched the catalog.</summary>
    public TimeSpan? Refresh { get; init; }
    /// <summary>How long this request spent checking the saved history around the refresh (the transcript's identity, a Hermes session's existence); null on a path without checks.</summary>
    public TimeSpan? Checks { get; init; }
    public required DateTimeOffset Now { get; init; }

    /// <summary>The notice's words.</summary>
    public string Summary => (Running ? "This pane is already running" : "This pane is still starting") + "; the second start was skipped.";

    /// <summary>The conflict in full, for the diagnostics log.</summary>
    public string Describe()
    {
        var state = Running ? "running" : "starting";
        var text = new StringBuilder(Running ? "This pane is already running" : "This pane is still starting")
            .Append(", so this request to start it was skipped and the ").Append(state).Append(" terminal left as it is. ")
            .Append(Agent).Append(Tab is null ? " in a pane that has since closed" : " in tab “" + Tab + "”");
        if (PaneCount > 1) text.Append(", pane ").Append(PaneNumber).Append(" of ").Append(PaneCount);
        if (SessionId is { Length: > 0 } session) text.Append(", session ").Append(session);
        text.Append(". This request came from ").Append(Request.Trigger).Append(' ').Append(Elapsed(Now - Request.At)).Append(" ago")
            .Append(Refresh is not { } refresh ? ""
                : "; its history refresh took " + Elapsed(refresh) + (Checks is { } checks ? " and its saved-history checks " + Elapsed(checks) : ""))
            .Append('.');
        var gap = Holder.At - Request.At;
        text.Append(" The ").Append(state).Append(" terminal was asked for ")
            .Append(Math.Abs(gap.TotalSeconds) < 0.05 ? "at the same moment" : Elapsed(gap.Duration()) + (gap < TimeSpan.Zero ? " earlier" : " later"))
            .Append(", from ").Append(Holder.Trigger)
            .Append(!Running ? ", and its terminal has not finished loading."
                : ProcessId is not { } process ? "."
                : ProcessStartedAt is { } started ? ", and has run as process " + process.ToString(CultureInfo.InvariantCulture) + " for " + Elapsed(Now - started) + "."
                : ", and runs as process " + process.ToString(CultureInfo.InvariantCulture) + ".");
        return text.ToString();
    }

    /// <summary>A span as the notice reads it: tenths of a second under 10 s, whole seconds under 2 minutes, then minutes, then hours.</summary>
    public static string Elapsed(TimeSpan span)
    {
        var seconds = Math.Max(0, span.TotalSeconds);
        return seconds < 10 ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : seconds < 120 ? Math.Round(seconds).ToString(CultureInfo.InvariantCulture) + " s"
            : seconds < 7200 ? Math.Round(seconds / 60).ToString(CultureInfo.InvariantCulture) + " min"
            : (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h";
    }
}
