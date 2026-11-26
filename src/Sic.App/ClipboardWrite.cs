using Sic.Core.Shell;
using Windows.ApplicationModel.DataTransfer;

namespace Sic.App;

/// <summary>
/// Every copy SicTerm makes: a program's own (OSC 52), copy on select, the pane menu's Copy and the Sessions panel's Copy session ID.
/// Written with the WinRT clipboard and tried again while another program holds the clipboard open (<see cref="ClipboardRetry"/>),
/// on the UI thread, so copies land in the order they were made. A write that needed more than one try is logged.
/// </summary>
internal static class ClipboardWrite
{
    /// <summary>Puts <paramref name="text"/> on the clipboard; returns the try that wrote it, and throws when the clipboard stayed held.</summary>
    internal static int SetText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        var attempt = ClipboardRetry.Run(() => Clipboard.SetContent(package));
        if (attempt > 1) Diagnostics.Log("clipboard: held by another program; written on try " + attempt);
        return attempt;
    }
}
