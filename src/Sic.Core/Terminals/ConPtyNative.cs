using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Sic.Core.Terminals;

internal static class ConPtyNative
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint UseStandardHandles = 0x00000100;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const nuint PseudoConsoleAttribute = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Coord(short x, short y)
    {
        internal readonly short X = x;
        internal readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        internal int Size;
        internal IntPtr Reserved;
        internal IntPtr Desktop;
        internal IntPtr Title;
        internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        internal short ShowWindow, Reserved2Size;
        internal IntPtr Reserved2, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal IntPtr ProcessHandle, ThreadHandle;
        internal int ProcessId, ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        internal ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobExtendedLimitInformation
    {
        internal JobBasicLimitInformation BasicLimits;
        internal IoCounters IoCounters;
        internal nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        ref JobExtendedLimitInformation information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    internal static void Check(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, uint size);

    // Windows' own console host, the fallback (docs/decisions/console-host.md).
    [DllImport("kernel32.dll")]
    internal static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(IntPtr console);

    [DllImport("kernel32.dll")]
    internal static extern int ResizePseudoConsole(IntPtr console, Coord size);

    /// <summary>
    /// Microsoft's packaged console host (NuGet Microsoft.Windows.Console.ConPTY, from github.com/microsoft/terminal):
    /// conpty.dll beside SIC, which starts OpenConsole.exe from the folder beside it named for the machine's own architecture
    /// (x64\ or arm64\). Loaded only from SIC's own folder or System32, never from PATH or the working folder.
    /// </summary>
    internal const string PackagedLibrary = "conpty.dll";
    internal const DllImportSearchPath PackagedSearch = DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories;
    /// <summary>
    /// PSEUDOCONSOLE_GLYPH_WIDTH_GRAPHEMES (conpty.h): the host counts the columns of its own buffer by grapheme cluster, as
    /// the renderer does, so what a program reads back through the console API (its cursor position) matches what is drawn.
    /// </summary>
    internal const uint GlyphWidthGraphemes = 0x08;

    [DllImport(PackagedLibrary), DefaultDllImportSearchPaths(PackagedSearch)]
    internal static extern int ConptyCreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    [DllImport(PackagedLibrary), DefaultDllImportSearchPaths(PackagedSearch)]
    internal static extern int ConptyResizePseudoConsole(IntPtr console, Coord size);

    [DllImport(PackagedLibrary), DefaultDllImportSearchPaths(PackagedSearch)]
    internal static extern void ConptyClosePseudoConsole(IntPtr console);

    /// <summary>The entry points SIC calls; a conpty.dll without any of them is not used.</summary>
    internal static readonly string[] PackagedEntryPoints = ["ConptyCreatePseudoConsole", "ConptyResizePseudoConsole", "ConptyClosePseudoConsole"];

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetModuleFileNameW(IntPtr module, [Out] char[] fileName, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

    /// <summary>GetCurrentProcess's pseudo-handle, which needs no closing.</summary>
    internal static readonly IntPtr CurrentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment,
        string workingDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
