using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sic.Core.Terminals;

/// <summary>
/// The console host SIC's pseudoconsoles run in. Microsoft's packaged host (conpty.dll with OpenConsole.exe, shipped beside
/// SIC) passes what the program writes through to the renderer; Windows' own (kernel32) re-renders its buffer and is kept as
/// the fallback for a copy where the packaged host does not load or cannot start. The choice is made once per process and
/// kept: every later pseudoconsole starts in the same host. See docs/decisions/console-host.md.
/// </summary>
public static class ConsoleHost
{
    public const string Packaged = "packaged";
    public const string Inbox = "inbox";

    private static readonly object Gate = new();
    private static bool _probed;
    private static string? _fallbackReason;
    private static string? _packagedVersion;

    /// <summary><see cref="Packaged"/> unless the packaged host was found unusable, then <see cref="Inbox"/>.</summary>
    public static string Current => UsePackaged() ? Packaged : Inbox;

    /// <summary>Why Windows' own host is in use; null while the packaged one is.</summary>
    public static string? FallbackReason
    {
        get { Probe(); lock (Gate) return _fallbackReason; }
    }

    /// <summary>The packaged host's version (conpty.dll's product version, which is the package's), null when it did not load.</summary>
    public static string? PackagedVersion
    {
        get { Probe(); lock (Gate) return _packagedVersion; }
    }

    internal static bool UsePackaged()
    {
        Probe();
        lock (Gate) return _fallbackReason is null;
    }

    /// <summary>Stops using the packaged host for the rest of the process; the first reason given is the one kept.</summary>
    internal static void FallBack(string reason)
    {
        lock (Gate) _fallbackReason ??= reason;
    }

    /// <summary>
    /// The failure of a packaged create in words, for a fallback: conpty.dll loaded, but OpenConsole.exe did not start (missing
    /// from the x64 or arm64 folder, or blocked by policy).
    /// </summary>
    internal static string CreateFailure(int result)
    {
        // A Win32 error carried as an HRESULT (FACILITY_WIN32) reads best as the Win32 error itself.
        var message = unchecked((uint)result) >> 16 == 0x8007 ? new Win32Exception(result & 0xFFFF).Message : Marshal.GetExceptionForHR(result)?.Message;
        // The first sentence only: Windows' longer messages go on to advice for someone else's program.
        var sentence = message?.Replace("%1", "OpenConsole.exe").Split(". ")[0].TrimEnd('.');
        return $"conpty.dll could not start OpenConsole.exe: {sentence} (0x{result:X8})";
    }

    /// <summary>
    /// A packaged session whose program ended on its own before the host had written anything: the host never ran (it opens
    /// every session by writing its startup sequence), so later sessions use Windows' own.
    /// </summary>
    internal static void SilentHost(int exitCode) =>
        FallBack($"OpenConsole.exe wrote nothing before the program in it ended (exit code 0x{exitCode:X8})");

    /// <summary>
    /// Loads conpty.dll as the P/Invokes will, from SIC's own folder, checks it has every entry point SIC calls, and checks that
    /// OpenConsole.exe is where it will look. The library stays loaded for those calls.
    /// </summary>
    private static void Probe()
    {
        lock (Gate)
        {
            if (_probed) return;
            _probed = true;
            try
            {
                var library = NativeLibrary.Load(ConPtyNative.PackagedLibrary, typeof(ConsoleHost).Assembly, ConPtyNative.PackagedSearch);
                var missing = ConPtyNative.PackagedEntryPoints.FirstOrDefault(name => !NativeLibrary.TryGetExport(library, name, out _));
                if (missing is not null)
                {
                    _fallbackReason = "conpty.dll has no " + missing;
                    return;
                }
                var buffer = new char[32768];
                var length = ConPtyNative.GetModuleFileNameW(library, buffer, (uint)buffer.Length);
                if (length == 0 || length >= buffer.Length)
                {
                    _fallbackReason = "the folder conpty.dll was loaded from could not be read";
                    return;
                }
                var path = new string(buffer, 0, (int)length);
                // With OpenConsole.exe in neither place, ConptyCreatePseudoConsole still reported success on October 6, 2026, and
                // the program in it exited at once without a byte of output; so it is looked for first.
                if (OpenConsolePath(Path.GetDirectoryName(path)!) is null)
                {
                    _fallbackReason = $"OpenConsole.exe is not in the {NativeArchitectureFolder() ?? "machine's"} folder beside conpty.dll";
                    return;
                }
                _packagedVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            }
            catch (DllNotFoundException) { _fallbackReason = "conpty.dll was not found beside SicTerm"; }
            catch (BadImageFormatException) { _fallbackReason = "conpty.dll is not built for this process"; }
        }
    }

    /// <summary>
    /// Where conpty.dll starts OpenConsole.exe from: beside itself, else in the folder named for the machine's own architecture
    /// (both seen working on an x64 PC, October 6, 2026). For an x64 SIC under emulation on an ARM64 PC that is arm64, which is
    /// why the package ships an arm64 host with an x64 app; that case has not been run. Null when it is in neither.
    /// </summary>
    internal static string? OpenConsolePath(string conptyFolder)
    {
        var beside = Path.Combine(conptyFolder, "OpenConsole.exe");
        if (File.Exists(beside)) return beside;
        return NativeArchitectureFolder() is { } folder && File.Exists(Path.Combine(conptyFolder, folder, "OpenConsole.exe"))
            ? Path.Combine(conptyFolder, folder, "OpenConsole.exe") : null;
    }

    /// <summary>The machine's own architecture as conpty.dll names its folders (IsWow64Process2's native machine).</summary>
    private static string? NativeArchitectureFolder() =>
        ConPtyNative.IsWow64Process2(ConPtyNative.CurrentProcess, out _, out var native) ? native switch
        {
            0x8664 => "x64",   // IMAGE_FILE_MACHINE_AMD64
            0xAA64 => "arm64", // IMAGE_FILE_MACHINE_ARM64
            0x014C => "x86",   // IMAGE_FILE_MACHINE_I386
            _ => null
        } : null;
}
