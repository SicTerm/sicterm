using System.Globalization;
using System.Text;

namespace Sic.Core.Terminals;

/// <summary>How the window tells of a program's own copy (OSC 52): not at all, with a brief notice, or with one that stays.</summary>
public enum ProgramCopyNotice { None, Brief, Held }

/// <summary>
/// Text crossing the clipboard into and out of a terminal (docs/decisions/clipboard.md). A paste is filtered as Windows Terminal
/// filters every paste (ControlCore::PasteText through Utils::FilterStringForPaste): each line break becomes one carriage
/// return, the Enter a terminal sends, and every other control character goes but the tab. Since 0.9.25 a program in any pane can
/// put text on the clipboard (OSC 52), so the clipboard can hold what a program printed; without the filter such text could
/// carry an escape sequence through a paste, the end of a bracketed paste (ESC [ 2 0 1 ~) among them, after which the rest
/// arrives as typed keys.
/// </summary>
public static class ClipboardText
{
    /// <summary>
    /// How soon after the user's own key, click or drag in a pane a program's copy counts as asked for: Claude Code copies as the
    /// mouse is released, within milliseconds. A copy later than this, or with no such action at all, was not the user's doing.
    /// </summary>
    public static readonly TimeSpan AskedForWithin = TimeSpan.FromSeconds(2);

    private const string Blank = " \t\r\n";

    /// <summary>
    /// Text as a paste sends it: CR LF and a lone LF become CR; C0 controls but tab and CR, DEL, and C1 controls are dropped.
    /// Everything else, other scripts and emoji included, goes through as it is.
    /// </summary>
    public static string ForPaste(string text)
    {
        var pasted = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\n') { if (index == 0 || text[index - 1] != '\r') pasted.Append('\r'); }
            else if (!IsControl(character)) pasted.Append(character);
        }
        return pasted.ToString();
    }

    private static bool IsControl(char character) => character is (< ' ' and not ('\t' or '\r')) or (>= '\u007f' and <= '\u009f');

    /// <summary>
    /// One line without its trailing whitespace, line break included, as Windows Terminal's trimPaste (on by default) leaves it,
    /// so a command copied with its line break waits for Enter instead of running as it is pasted. Text that breaks before its
    /// last non-blank character is several lines and comes back as it went in; text all blank comes back empty.
    /// </summary>
    public static string TrimSingleLine(string text)
    {
        var kept = text.AsSpan().TrimEnd(Blank);
        return kept.IndexOfAny('\r', '\n') >= 0 ? text : kept.ToString();
    }

    /// <summary>Whether the text breaks before its last non-blank character: a shell without bracketed paste runs each line as it arrives.</summary>
    public static bool IsMultiLine(string text) => text.AsSpan().TrimEnd(Blank).IndexOfAny('\r', '\n') >= 0;

    /// <summary>The lines a reader counts: blank lines inside the text count, line breaks after its last non-blank character do not.</summary>
    public static int LineCount(string text)
    {
        var kept = text.AsSpan().TrimEnd(Blank);
        var lines = 1;
        for (var index = 0; index < kept.Length; index++)
            if (kept[index] == '\n' ? index == 0 || kept[index - 1] != '\r' : kept[index] == '\r') lines++;
        return lines;
    }

    /// <summary>
    /// The start of copied text on one line, for a notice: a line break shown as ↵, tabs and runs of spaces as one space, other
    /// control characters dropped, and cut after <paramref name="length"/> characters with an ellipsis, never inside a character
    /// that takes two UTF-16 units or more (an emoji).
    /// </summary>
    public static string Preview(string text, int length = 60)
    {
        var line = new StringBuilder();
        var space = false;
        void Add(string piece) { if (space && line.Length > 0) line.Append(' '); space = false; line.Append(piece); }
        for (var index = 0; index < text.Length && line.Length <= length * 4; index++)
        {
            var character = text[index];
            if (character == '\n' && index > 0 && text[index - 1] == '\r') continue;
            if (character is '\r' or '\n') { space = true; Add("↵"); space = true; }
            else if (character is ' ' or '\t') space = true;
            else if (!IsControl(character)) Add(character.ToString());
        }
        var preview = line.ToString();
        var elements = StringInfo.ParseCombiningCharacters(preview);
        return elements.Length <= length ? preview : preview[..elements[length]].TrimEnd() + "…";
    }

    /// <summary>
    /// How a program's copy is told of. An agent says "copied" itself, so a copy that follows the user's own key, click or drag
    /// in its pane within <see cref="AskedForWithin"/> needs nothing more; in a PowerShell pane such copies are rare, and one gets a
    /// brief notice. A copy that follows no such action, in any pane, was asked for by whatever the pane printed (a file shown, a
    /// remote host), and the notice stays until it is closed. <paramref name="sinceAction"/> is how long before the copy the user's
    /// last action in the pane was, null when there was none.
    /// </summary>
    public static ProgramCopyNotice NoticeForProgramCopy(bool shell, TimeSpan? sinceAction) =>
        sinceAction is not { } since || since > AskedForWithin ? ProgramCopyNotice.Held : shell ? ProgramCopyNotice.Brief : ProgramCopyNotice.None;
}
