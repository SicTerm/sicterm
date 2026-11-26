using System.Runtime.InteropServices;

namespace Sic.Core.Limits;

/// <summary>
/// Where a status line reading came from: the SicTerm pane that started the Claude Code process (<see cref="PaneVariable"/>,
/// which SicTerm sets in a Claude Code pane's environment and every process under it inherits) and that Claude Code process
/// itself, the status line helper's nearest ancestor that is not a shell. A <c>/clear</c> or an in-pane <c>/resume</c> moves
/// the process to another session, and the pane follows it only when the reading comes from the process the pane runs
/// (docs/decisions/context-window.md): a Claude Code started in a window of its own from inside the pane inherits the
/// variable too, but is a process of its own.
/// </summary>
public static class PaneOrigin
{
    /// <summary>The environment variable that carries the pane's ID to the status line helper.</summary>
    public const string PaneVariable = "SICTERM_PANE";

    /// <summary>The programs that stand between Claude Code and the status line command it runs: the shells it may run it through.</summary>
    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash.exe", "sh.exe", "dash.exe", "zsh.exe", "cmd.exe", "pwsh.exe", "powershell.exe", "env.exe"
    };

    /// <summary>Whether <paramref name="id"/> can be a pane's ID: 1–64 letters, digits, hyphens or underscores (SicTerm's are 32 hex digits).</summary>
    public static bool IsPaneId(string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// The nearest ancestor of <paramref name="process"/> that is not a shell, from <paramref name="parentOf"/> (a process's
    /// parent ID and image name, or null when it is not running); null when the line ends first or runs past 16 steps.
    /// </summary>
    public static int? AgentProcess(int process, Func<int, (int Parent, string Image)?> parentOf)
    {
        var seen = new HashSet<int> { process };
        var current = process;
        for (var step = 0; step < 16; step++)
        {
            if (parentOf(current) is not { } entry || entry.Parent <= 0 || !seen.Add(entry.Parent)) return null;
            current = entry.Parent;
            if (parentOf(current) is not { } parent) return null;
            if (!Shells.Contains(parent.Image)) return current;
        }
        return null;
    }

    /// <summary>
    /// Whether a reading from the pane (its <see cref="ContextReading.Pane"/> already matched) moves the pane to the reading's
    /// session: it names another session than <paramref name="paneSession"/>, its Claude Code process is known, and it is the
    /// process the pane's own session reported from (<paramref name="paneProcess"/>), or none has reported yet.
    /// </summary>
    public static bool Moves(string? paneSession, int? paneProcess, ContextReading reading) =>
        reading.AgentProcess is { } agent && (paneProcess is null || paneProcess == agent)
        && !string.Equals(paneSession, reading.SessionId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The pane and the Claude Code process of the running status line helper, either null when not known.</summary>
    public static (string? Pane, int? AgentProcess) Current()
    {
        var pane = Environment.GetEnvironmentVariable(PaneVariable);
        if (!IsPaneId(pane)) return (null, null);
        try
        {
            var processes = Snapshot();
            return (pane, AgentProcess(Environment.ProcessId, id => processes.TryGetValue(id, out var entry) ? entry : null));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            return (pane, null);
        }
    }

    /// <summary>Every running process's parent and image name, from one Toolhelp snapshot.</summary>
    private static Dictionary<int, (int Parent, string Image)> Snapshot()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var processes = new Dictionary<int, (int, string)>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                processes[(int)entry.ProcessId] = ((int)entry.ParentProcessId, entry.ExeFile);
            return processes;
        }
        finally { CloseHandle(snapshot); }
    }

    private const uint SnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
