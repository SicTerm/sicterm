using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// One SIC per data folder. The running window tags itself with a key derived from its data folder; a second
/// launch with the same folder finds that window, asks it to come forward (which also restores it from the tray),
/// and exits. <c>SicTerm.exe --quit</c> finds it the same way and asks it to quit. Scratch data folders used by the smoke and
/// review scripts get their own key, so they stay independent.
/// </summary>
internal static class SingleInstance
{
    private const string Property = "SIC.Instance";
    private static Mutex? _mutex;
    internal static readonly uint ActivateMessage = RegisterWindowMessage("SIC.Activate");
    internal static readonly uint QuitMessage = RegisterWindowMessage("SIC.Quit");

    /// <summary>
    /// True when this process owns the data folder. False after the running instance has been asked to come forward, and handed
    /// <paramref name="open"/>, the folder <c>--open</c> named, when there is one.
    /// </summary>
    internal static bool TryAcquire(string dataDirectory, OpenRequest? open = null)
    {
        var key = Key(dataDirectory);
        _mutex = new Mutex(initiallyOwned: true, "Local\\SIC.SuperIntelligentChat." + key.ToString("x8"), out var createdNew);
        // The mutex is the fast path. A SIC started from inside the identity package's app container, as File Explorer's menu
        // starts one, has a named-object namespace of its own and does not see a mutex the running SIC made outside it
        // (October 7, 2026: a second SIC opened on the same data folder); the tagged window, on the shared desktop, is the truth.
        if (createdNew && Find(key) == 0) return true;
        ActivateExisting(key, open);
        return false;
    }

    /// <summary>Marks this window as the instance for its data folder so a later launch can find it even while hidden in the tray.</summary>
    internal static void Tag(nint window, string dataDirectory) => SetProp(window, Property, Key(dataDirectory));

    private static nint Key(string dataDirectory)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.TrimEnd('\\', '/').ToLowerInvariant()));
        var key = BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
        return key == 0 ? 1 : key;
    }

    /// <summary>
    /// Asks the SIC running with this data folder to quit as its tray menu's Quit does, so running agents are asked to exit, or
    /// the quit question shown, as usual. Nothing happens when none is running.
    /// </summary>
    internal static void RequestQuit(string dataDirectory)
    {
        var found = Find(Key(dataDirectory));
        if (found == 0) return;
        GetWindowThreadProcessId(found, out var pid);
        AllowSetForegroundWindow(pid);
        PostMessage(found, QuitMessage, 0, 0);
    }

    /// <summary>The window tagged as the instance for a data folder's key, or 0 when there is none.</summary>
    private static nint Find(nint key)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            if (GetProp(window, Property) != key) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    private static void ActivateExisting(nint key, OpenRequest? open)
    {
        var found = Find(key);
        var ownPath = Environment.ProcessPath ?? "";
        if (found == 0)
        {
            MessageBox(0, "SicTerm is already running with this data folder, but its window could not be found. It may be in the tray or in Task Manager.", "SicTerm", 0x30);
            return;
        }
        GetWindowThreadProcessId(found, out var pid);
        AllowSetForegroundWindow(pid);
        PostMessage(found, ActivateMessage, 0, 0);
        if (open is not null && !SendOpenRequest(found, open))
            MessageBox(0, "The SicTerm that is running did not take the folder\n" + open.Folder + "\n\nIt may be an older version. Once it quits, Open in SicTerm starts this version.", "SicTerm", 0x30);
        string? otherPath = null;
        try { otherPath = Process.GetProcessById((int)pid).MainModule?.FileName; }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
        if (otherPath is not null && !string.Equals(otherPath, ownPath, StringComparison.OrdinalIgnoreCase))
            MessageBox(0, "SicTerm is already running from\n" + otherPath + "\n\nThat window is in front now. This build starts once it quits:\n" + ownPath, "SicTerm", 0x40);
    }

    /// <summary>
    /// Hands the request to the running window in a <c>WM_COPYDATA</c> message, sent rather than posted, since the buffer lives
    /// only for the call; true when the window answered that it took it. A window that hangs is given ten seconds.
    /// </summary>
    private static bool SendOpenRequest(nint window, OpenRequest open)
    {
        var text = open.Encode();
        var buffer = Marshal.StringToHGlobalUni(text);
        try
        {
            var data = new CopyData { Id = OpenRequest.CopyDataId, Size = (uint)((text.Length + 1) * 2), Data = buffer };
            var block = Marshal.AllocHGlobal(Marshal.SizeOf<CopyData>());
            try
            {
                Marshal.StructureToPtr(data, block, fDeleteOld: false);
                return SendMessageTimeout(window, CopyDataMessage, 0, block, 0x0002 | 0x0001, 10_000, out var result) != 0 && result == 1;
            }
            finally { Marshal.FreeHGlobal(block); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal const uint CopyDataMessage = 0x004A;

    /// <summary>Win32's COPYDATASTRUCT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CopyData { internal nuint Id; internal uint Size; internal nint Data; }

    private delegate bool EnumWindowsProcedure(nint window, nint parameter);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", ExactSpelling = true)] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProcedure procedure, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint GetProp(nint window, string name);
    [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProp(nint window, string name, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int MessageBox(nint owner, string text, string caption, uint type);
}