using System.Runtime.InteropServices;

namespace Sic.App;

/// <summary>
/// The clipboard read with the Win32 calls, for a paste (docs/decisions/clipboard.md, "Reading the clipboard"). Until October 21,
/// 2026 the WinRT view (Clipboard.GetContent) read it, and in a Remote Desktop session text copied on the local PC pasted nothing
/// until some other program had fetched it first: that view answers a clipboard it cannot open with no formats and no error, where
/// these calls fail and can be retried, and Notepad, PowerShell and conhost paste the same clipboard through them. Windows Terminal
/// made the same change for the same reports (its PR #15360). Opened with a retry, since another program may hold the clipboard
/// for a moment (rdpclip.exe fetching from the client, a clipboard viewer reading), and read off the UI thread, since text owned
/// by rdpclip is rendered by a round trip to the client.
/// </summary>
internal static class Win32Clipboard
{
    /// <summary>What a paste takes from the clipboard: the files copied in Explorer, as their paths, or else the text.</summary>
    internal sealed record Content(string? Text, IReadOnlyList<string> Files);

    private const uint UnicodeText = 13, FileDrop = 15, FileCount = 0xFFFFFFFF;

    /// <summary>How long a busy clipboard is retried before the paste gives up: the waits double from 10 ms.</summary>
    private const int LastWait = 2560;

    /// <summary>The clipboard's files or text, read on a worker thread. Throws when another program held the clipboard for about five seconds.</summary>
    internal static Task<Content> ReadAsync() => Task.Run(Read);

    private static Content Read()
    {
        if (!Open())
        {
            Diagnostics.Log("Paste: the clipboard stayed open in another program for five seconds.");
            throw new InvalidOperationException("The clipboard is in use by another program.");
        }
        try
        {
            // Both are copied out before the clipboard is closed: a handle from GetClipboardData is the clipboard's own.
            var files = ReadFiles();
            return files.Count > 0 ? new Content(null, files) : new Content(ReadText(), []);
        }
        finally { CloseClipboard(); }
    }

    private static bool Open()
    {
        for (var wait = 10; ; wait *= 2)
        {
            if (OpenClipboard(0)) return true;
            if (wait > LastWait) return false;
            Thread.Sleep(wait);
        }
    }

    /// <summary>CF_UNICODETEXT, which Windows also makes from an ANSI or OEM text the owner put up. Null when there is none.</summary>
    private static string? ReadText()
    {
        var handle = GetClipboardData(UnicodeText);
        if (handle == 0) return null;
        var pointer = GlobalLock(handle);
        if (pointer == 0) return null;
        try
        {
            // Read to the handle's size, then to the first null: the owner may have given no terminator, or a shorter text.
            var text = Marshal.PtrToStringUni(pointer, (int)Math.Min(GlobalSize(handle) / 2, (nuint)int.MaxValue));
            var end = text.IndexOf('\0');
            return end < 0 ? text : text[..end];
        }
        finally { GlobalUnlock(handle); }
    }

    /// <summary>CF_HDROP, what Explorer's Copy puts up: each file's path, in order.</summary>
    private static List<string> ReadFiles()
    {
        var files = new List<string>();
        var handle = GetClipboardData(FileDrop);
        if (handle == 0) return files;
        var count = DragQueryFileW(handle, FileCount, null, 0);
        for (var index = 0u; index < count; index++)
        {
            var length = DragQueryFileW(handle, index, null, 0);
            if (length == 0) continue;
            var buffer = new char[length + 1];
            var copied = DragQueryFileW(handle, index, buffer, length + 1);
            if (copied > 0) files.Add(new string(buffer, 0, (int)copied));
        }
        return files;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint handle);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint DragQueryFileW(nint drop, uint index, [Out] char[]? name, uint capacity);
}
