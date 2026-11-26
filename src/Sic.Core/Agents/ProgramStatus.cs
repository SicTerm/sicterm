using System.Globalization;

namespace Sic.Core.Agents;

/// <summary>A program's state as it reports it over OSC 7501, the Program Status Protocol (docs/decisions/program-status.md).</summary>
public enum ProgramState
{
    /// <summary>At rest, waiting for the user's next instruction.</summary>
    Idle,
    /// <summary>Running.</summary>
    Working,
    /// <summary>Finished work whose result the user has not seen.</summary>
    Done,
    /// <summary>Cannot go on until the user acts: a permission, a question or a sign-in (<see cref="ProgramStatus.Kind"/>).</summary>
    Blocked,
    /// <summary>Failed and stopped.</summary>
    Error,
    /// <summary>Not a state: the program withdraws its record.</summary>
    Clear
}

/// <summary>
/// One report of the Program Status Protocol, OSC 7501: <c>ESC ] 7501 ; key=value:key=value ST</c>, which Claude Code (from
/// 2.1.295), Codex (from 0.163) and OpenCode (from 2.2) send so a terminal can show whether they work, wait for the user or are
/// done without reading their screens (docs/decisions/program-status.md). <see cref="Parse"/> takes the body after "7501;" as
/// the specification's revision 0.4 reads it: pairs split on ':', each key letters a to z, each value letters, digits and
/// "_.,+/=-", both trimmed; a malformed pair is skipped, the last of a repeated key kept, and the whole report dropped for an
/// unknown or missing state or anything over a limit. The free text (<c>title</c> and <c>msg</c>, base64) is not read:
/// nothing a program says is shown as text.
/// </summary>
/// <param name="State">The reported state.</param>
/// <param name="Id">The record's path, such as "build/test"; null for the root record, the program's own state.</param>
/// <param name="Kind">With <see cref="ProgramState.Blocked"/>, what is needed: "permission", "question" or "auth"; otherwise null.</param>
/// <param name="Progress">With Working or Blocked, 0 to 100 when the program says how far along it is.</param>
/// <param name="App">The program's name for itself, when it gave one.</param>
public sealed record ProgramStatus(ProgramState State, string? Id = null, string? Kind = null, int? Progress = null, string? App = null)
{
    /// <summary>The most characters a report's body may have; a longer one is dropped whole, as the specification says.</summary>
    public const int BodyLimit = 4096;
    public const int KeyLimit = 16;
    public const int AppLimit = 32;
    public const int IdLimit = 128;
    public const int IdSegmentLimit = 32;
    public const int IdDepthLimit = 8;

    /// <summary>The body a program sends to ask whether the terminal listens; the terminal answers with the same body.</summary>
    public const string Query = "?";

    /// <summary>The report the body carries, or null for one to drop.</summary>
    public static ProgramStatus? Parse(string body)
    {
        if (body.Length == 0 || body.Length > BodyLimit || body == Query) return null;
        ProgramState? state = null;
        string? id = null, kind = null, app = null;
        int? progress = null;
        foreach (var pair in body.Split(':'))
        {
            var equals = pair.IndexOf('=');
            if (equals < 0) continue;
            var key = pair[..equals].Trim();
            var value = pair[(equals + 1)..].Trim();
            if (key.Length == 0 || key.Length > KeyLimit || !key.All(c => c is >= 'a' and <= 'z') || !value.All(IsValueChar)) continue;
            switch (key)
            {
                case "state":
                    state = value switch
                    {
                        "idle" => ProgramState.Idle, "working" => ProgramState.Working, "done" => ProgramState.Done,
                        "blocked" => ProgramState.Blocked, "error" => ProgramState.Error, "clear" => ProgramState.Clear, _ => null
                    };
                    if (state is null) return null;
                    break;
                case "id":
                    if (!IsId(value)) return null;
                    id = value.Length == 0 ? null : value;
                    break;
                case "kind":
                    kind = value is "permission" or "question" or "auth" ? value : null;
                    break;
                case "progress":
                    progress = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && percent <= 100 ? percent : null;
                    break;
                case "app":
                    if (value.Length > AppLimit) return null;
                    app = value.Length == 0 ? null : value;
                    break;
            }
        }
        if (state is not { } reported) return null;
        return new(reported, id, reported == ProgramState.Blocked ? kind : null, reported is ProgramState.Working or ProgramState.Blocked ? progress : null, app);
    }

    private static bool IsValueChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ',' or '+' or '/' or '=' or '-';

    private static bool IsId(string id)
    {
        if (id.Length > IdLimit) return false;
        if (id.Length == 0) return true;
        var segments = id.Split('/');
        return segments.Length <= IdDepthLimit && segments.All(segment => segment.Length is > 0 and <= IdSegmentLimit);
    }
}
