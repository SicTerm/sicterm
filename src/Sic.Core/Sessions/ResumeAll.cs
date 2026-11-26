using System.Globalization;

namespace Sic.Core.Sessions;

/// <summary>What a pane can do for Resume all, as read at the moment the plan is made.</summary>
public enum PaneStartState
{
    /// <summary>A terminal runs in it, or a start of it is already under way; nothing to do.</summary>
    Running,
    /// <summary>Its close, or its tab's, has begun; it takes no new agent.</summary>
    Closing,
    /// <summary>An agent pane with no saved conversation to resume; Link attaches one.</summary>
    Unlinked,
    /// <summary>A placeholder, or a terminal that exited, with a saved conversation or a shell to start.</summary>
    Resumable,
    /// <summary>A placeholder whose agent is switched off in Settings; it is left as it is until the agent is on again.</summary>
    AgentOff
}

/// <summary>One tab's turn: its index among the tabs the plan was made from, and the panes in it to start.</summary>
public sealed record ResumeAllStep(int Tab, IReadOnlyList<int> Panes);

/// <summary>The panes Resume all will start, tab by tab, and the ones it leaves alone, by reason.</summary>
public sealed record ResumeAllPlan(IReadOnlyList<ResumeAllStep> Steps, int Running, int Unlinked, int LeftOver, int AgentOff = 0)
{
    public int Requested => Steps.Sum(step => step.Panes.Count);
}

/// <summary>How a run went, for the one notice that stands in for a notice per pane.</summary>
public sealed record ResumeAllResult(int Requested, int Started, IReadOnlyList<string> Failures, int Running, int Unlinked, int LeftOver, bool Stopped, int AgentOff = 0);

/// <summary>
/// Resume all: every placeholder in every tab, in one request. The tabs are taken in strip order with the selected tab
/// first, since that is the one on screen; within a tab, its panes in order. The run starts no more panes than the live
/// limit leaves room for and reports the rest, and it says at the end, in one notice, what started and what did not.
/// </summary>
public static class ResumeAll
{
    /// <summary>Panes that may run at once in this version.</summary>
    public const int LiveLimit = 32;

    /// <param name="tabs">Each tab's panes, in strip order.</param>
    /// <param name="selectedTab">The selected tab's index, or -1 when none is.</param>
    /// <param name="live">Panes that hold a terminal or are getting one now; they take up the limit first.</param>
    public static ResumeAllPlan Plan(IReadOnlyList<IReadOnlyList<PaneStartState>> tabs, int selectedTab, int live, int limit = LiveLimit)
    {
        var room = Math.Max(0, limit - live);
        var steps = new List<ResumeAllStep>();
        int running = 0, unlinked = 0, leftOver = 0, agentOff = 0;
        foreach (var tab in Enumerable.Range(0, tabs.Count).OrderBy(index => index == selectedTab ? 0 : 1).ThenBy(index => index))
        {
            var panes = new List<int>();
            for (var pane = 0; pane < tabs[tab].Count; pane++)
            {
                switch (tabs[tab][pane])
                {
                    case PaneStartState.Running: running++; break;
                    case PaneStartState.Unlinked: unlinked++; break;
                    case PaneStartState.AgentOff: agentOff++; break;
                    case PaneStartState.Closing: break;
                    case PaneStartState.Resumable:
                        if (room > 0) { panes.Add(pane); room--; }
                        else leftOver++;
                        break;
                }
            }
            if (panes.Count > 0) steps.Add(new(tab, panes));
        }
        return new(steps, running, unlinked, leftOver, agentOff);
    }

    /// <summary>
    /// The notice: its title, its message, and whether anything went short of the request. The message holds only what the
    /// tabs do not show (a pane that did not start and why, one left unlinked or with its agent off), so a clean run's is
    /// empty and the notice is its title alone. Each failure is a line that names its pane.
    /// </summary>
    public static (string Title, string Message, bool Trouble) Summarize(ResumeAllResult result, int limit = LiveLimit)
    {
        var wanted = result.Requested + result.LeftOver;
        var trouble = result.Failures.Count > 0 || result.LeftOver > 0 || result.Stopped;
        var title = result.Stopped ? "Resume all stopped"
            : wanted == 0 ? "Nothing to resume"
            : !trouble ? "Resumed " + Sessions(result.Started)
            : "Resumed " + result.Started.ToString(CultureInfo.InvariantCulture) + " of " + Sessions(wanted);
        var parts = new List<string>();
        if (result.Stopped) parts.Add("SicTerm is quitting; the rest were not started.");
        parts.AddRange(result.Failures);
        if (result.LeftOver > 0) parts.Add(result.LeftOver.ToString(CultureInfo.InvariantCulture) + " not started: the limit is " + limit.ToString(CultureInfo.InvariantCulture) + " panes running at once, across all tabs.");
        if (result.Unlinked > 0) parts.Add(result.Unlinked.ToString(CultureInfo.InvariantCulture) + " not linked to a saved conversation.");
        if (result.AgentOff > 0) parts.Add(result.AgentOff.ToString(CultureInfo.InvariantCulture) + " not started: " + (result.AgentOff == 1 ? "its agent is" : "their agents are") + " off in Settings → Agents.");
        if (parts.Count == 0 && wanted == 0 && result.Running > 0) parts.Add("All sessions are already running.");
        return (title, string.Join("\n", parts), trouble);
    }

    private static string Sessions(int count) => count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " session" : " sessions");
}
