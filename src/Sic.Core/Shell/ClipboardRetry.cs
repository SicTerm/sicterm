namespace Sic.Core.Shell;

/// <summary>
/// A clipboard write retried while another program holds the clipboard open (docs/decisions/clipboard.md, "Writing the
/// clipboard"). Windows lets one program at a time open the clipboard, and a write that finds it open fails at once with
/// CLIPBRD_E_CANT_OPEN; in a Remote Desktop session rdpclip.exe opens it right after every change to tell the client, and a
/// clipboard history or manager does the same, so a copy a few milliseconds after another was lost (November 11, 2026: the
/// smoke's second OSC 52 copy, twice in 59 runs on an RDP session). The write is tried again after 10, 20, 40 and 80 ms, 150 ms
/// in all, on the thread that writes, so two copies in a row still land in their order; the last failure is thrown.
/// </summary>
public static class ClipboardRetry
{
    /// <summary>CLIPBRD_E_CANT_OPEN: OpenClipboard failed, since another window has the clipboard open.</summary>
    public const int CantOpen = unchecked((int)0x800401D0);
    /// <summary>The waits between tries; a write that still finds the clipboard held after the last one fails.</summary>
    public static readonly IReadOnlyList<TimeSpan> Waits = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80)];

    /// <summary>
    /// Runs <paramref name="write"/> until it does not fail with <see cref="CantOpen"/>, waiting between tries with
    /// <paramref name="wait"/> (Thread.Sleep by default). Returns the try that wrote; any other failure, or the last busy one, is thrown.
    /// </summary>
    public static int Run(Action write, Action<TimeSpan>? wait = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                write();
                return attempt;
            }
            catch (Exception exception) when (exception.HResult == CantOpen && attempt <= Waits.Count)
            {
                (wait ?? Thread.Sleep)(Waits[attempt - 1]);
            }
        }
    }
}
