using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace Sic.Core.Terminals;

/// <summary>Owns one real Windows pseudoconsole and its initially suspended child process.</summary>
public sealed class ConPtySession : IAsyncDisposable
{
    private readonly object _consoleGate = new();
    private readonly Process _process;
    private readonly SafeFileHandle _job;
    private readonly FileStream _input;
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly FileStream _output;
    private readonly CancellationTokenSource _discardOutput = new();
    private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false
    });
    private readonly Task _readTask;
    private readonly bool _packaged;
    private IntPtr _console;
    private int _disposed;
    private bool _received;

    public ChannelReader<byte[]> Output => _channel.Reader;
    public Task<int> Completion { get; }
    public int ProcessId { get; }
    /// <summary>
    /// The console host this session's pseudoconsole runs in, <see cref="ConsoleHost.Packaged"/> or <see cref="ConsoleHost.Inbox"/>;
    /// its resize and close go to the same host.
    /// </summary>
    public string Host => _packaged ? ConsoleHost.Packaged : ConsoleHost.Inbox;

    private ConPtySession(IntPtr console, bool packaged, SafeFileHandle input, SafeFileHandle output, Process process, SafeFileHandle job)
    {
        _console = console;
        _packaged = packaged;
        _process = process;
        _job = job;
        ProcessId = process.Id;
        // Unbuffered too: every write is flushed at once (a keystroke, a paste), so a buffer would only copy each one first.
        _input = new FileStream(input, FileAccess.Write, 0, isAsync: false);
        // Unbuffered: ReadOutput reads into its own buffer, as large as the pipe, so a burst comes out in a few reads rather than many.
        _output = new FileStream(output, FileAccess.Read, 1, isAsync: false);
        // ConPTY pipes are synchronous. Keep the reader off both the UI and writer threads.
        _readTask = Task.Factory.StartNew(ReadOutput, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Completion = ObserveExitAsync();
    }

    public async Task WriteInputAsync(string input, CancellationToken cancellationToken = default)
    {
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || Completion.IsCompleted, this);
            await _input.WriteAsync(Encoding.UTF8.GetBytes(input), cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inputGate.Release();
        }
    }

    public void Resize(int columns, int rows)
    {
        ValidateSize(columns, rows);
        lock (_consoleGate)
        {
            ObjectDisposedException.ThrowIf(_console == IntPtr.Zero, this);
            var size = new ConPtyNative.Coord(checked((short)columns), checked((short)rows));
            Marshal.ThrowExceptionForHR(_packaged ? ConPtyNative.ConptyResizePseudoConsole(_console, size) : ConPtyNative.ResizePseudoConsole(_console, size));
        }
    }

    /// <summary>Starts the request in the packaged console host, or in Windows' own when the packaged one cannot be used (<see cref="ConsoleHost"/>).</summary>
    public static ConPtySession Start(TerminalLaunchRequest request) => Start(request, preferPackaged: true);

    /// <summary>
    /// Creates the pseudoconsole in the packaged host when <paramref name="preferPackaged"/> and the process has not fallen back,
    /// else in Windows' own. A packaged create that fails where Windows' own then succeeds makes the fallback for the rest of the
    /// process; one that fails in both hosts is reported as the packaged host's error and changes nothing.
    /// </summary>
    private static int CreatePseudoConsole(ConPtyNative.Coord size, SafeFileHandle input, SafeFileHandle output, bool preferPackaged,
        out IntPtr console, out bool packaged)
    {
        packaged = preferPackaged && ConsoleHost.UsePackaged();
        if (packaged)
        {
            int result;
            try { result = ConPtyNative.ConptyCreatePseudoConsole(size, input, output, ConPtyNative.GlyphWidthGraphemes, out console); }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                ConsoleHost.FallBack("conpty.dll did not load: " + exception.Message);
                packaged = false;
                return ConPtyNative.CreatePseudoConsole(size, input, output, 0, out console);
            }
            if (result >= 0) return result;
            var inbox = ConPtyNative.CreatePseudoConsole(size, input, output, 0, out console);
            if (inbox < 0)
            {
                console = IntPtr.Zero;
                return result;
            }
            ConsoleHost.FallBack(ConsoleHost.CreateFailure(result));
            packaged = false;
            return inbox;
        }
        return ConPtyNative.CreatePseudoConsole(size, input, output, 0, out console);
    }

    private static void ClosePseudoConsole(IntPtr console, bool packaged)
    {
        if (packaged) ConPtyNative.ConptyClosePseudoConsole(console);
        else ConPtyNative.ClosePseudoConsole(console);
    }

    /// <summary>For tests: <paramref name="preferPackaged"/> false starts in Windows' own host without recording a fallback.</summary>
    internal static ConPtySession Start(TerminalLaunchRequest request, bool preferPackaged)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSize(request.Columns, request.Rows);
        if (!Path.IsPathFullyQualified(request.Executable))
        {
            throw new ArgumentException("The executable must be an absolute path resolved before launch.", nameof(request));
        }
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null;
        IntPtr console = IntPtr.Zero, attributes = IntPtr.Zero, environment = IntPtr.Zero;
        var packaged = false;
        var attributesInitialized = false;
        var processInformation = new ConPtyNative.ProcessInformation();
        ConPtySession? session = null;
        Process? process = null;
        SafeFileHandle? job = null;
        try
        {
            ConPtyNative.Check(ConPtyNative.CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0), "CreatePipe(input)");
            // The output pipe takes a whole burst of the host's output at once (the default, 4 KB, cut an agent's screen redraw
            // into dozens of reads, each a trip to the renderer), and ReadOutput's buffer matches it.
            ConPtyNative.Check(ConPtyNative.CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, OutputPipeSize), "CreatePipe(output)");
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(new ConPtyNative.Coord(checked((short)request.Columns), checked((short)request.Rows)),
                inputRead, outputWrite, preferPackaged, out console, out packaged));

            nuint attributeSize = 0;
            _ = ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            attributes = Marshal.AllocHGlobal(checked((nint)attributeSize));
            ConPtyNative.Check(ConPtyNative.InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeSize), "InitializeProcThreadAttributeList");
            attributesInitialized = true;
            ConPtyNative.Check(ConPtyNative.UpdateProcThreadAttribute(attributes, 0, ConPtyNative.PseudoConsoleAttribute,
                console, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute");

            var startup = new ConPtyNative.StartupInfoEx
            {
                // Explicit null standard handles prevent redirected parent I/O from bypassing ConPTY.
                // https://github.com/microsoft/terminal/discussions/15814
                StartupInfo = new ConPtyNative.StartupInfo
                {
                    Size = Marshal.SizeOf<ConPtyNative.StartupInfoEx>(),
                    Flags = ConPtyNative.UseStandardHandles
                },
                AttributeList = attributes
            };
            var arguments = new[] { request.Executable }.Concat(request.Arguments).Select(WindowsCommandLine.QuoteArgument);
            var commandLine = new StringBuilder(string.Join(" ", arguments));
            environment = CreateEnvironmentBlock(request.Environment);
            ConPtyNative.Check(ConPtyNative.CreateProcessW(request.Executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                ConPtyNative.ExtendedStartupInfoPresent | ConPtyNative.CreateSuspended | ConPtyNative.CreateUnicodeEnvironment, environment,
                request.WorkingDirectory, ref startup, out processInformation), "CreateProcessW");

            job = ConPtyNative.CreateJobObjectW(IntPtr.Zero, null);
            ConPtyNative.Check(!job.IsInvalid, "CreateJobObjectW");
            var limits = new ConPtyNative.JobExtendedLimitInformation
            {
                BasicLimits = new ConPtyNative.JobBasicLimitInformation { LimitFlags = 0x00002000 } // KILL_ON_JOB_CLOSE
            };
            ConPtyNative.Check(ConPtyNative.SetInformationJobObject(job, 9, ref limits,
                (uint)Marshal.SizeOf<ConPtyNative.JobExtendedLimitInformation>()), "SetInformationJobObject");
            // Assign while suspended so descendants cannot escape the owning terminal's lifetime.
            ConPtyNative.Check(ConPtyNative.AssignProcessToJobObject(job, processInformation.ProcessHandle), "AssignProcessToJobObject");
            process = Process.GetProcessById(processInformation.ProcessId);
            _ = process.SafeHandle; // Capture the exact process identity before it is allowed to exit.
            session = new ConPtySession(console, packaged, inputWrite, outputRead, process, job);
            job = null;
            console = IntPtr.Zero;
            inputWrite = null;
            outputRead = null;
            process = null;
            ConPtyNative.Check(ConPtyNative.ResumeThread(processInformation.ThreadHandle) != uint.MaxValue, "ResumeThread");
            return session;
        }
        catch
        {
            if (processInformation.ProcessHandle != IntPtr.Zero)
            {
                _ = ConPtyNative.TerminateProcess(processInformation.ProcessHandle, 1);
            }
            if (session is not null)
            {
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            throw;
        }
        finally
        {
            if (attributesInitialized) ConPtyNative.DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            if (processInformation.ThreadHandle != IntPtr.Zero) _ = ConPtyNative.CloseHandle(processInformation.ThreadHandle);
            if (processInformation.ProcessHandle != IntPtr.Zero) _ = ConPtyNative.CloseHandle(processInformation.ProcessHandle);
            inputRead?.Dispose();
            outputWrite?.Dispose();
            inputWrite?.Dispose();
            outputRead?.Dispose();
            process?.Dispose();
            job?.Dispose();
            if (console != IntPtr.Zero) ClosePseudoConsole(console, packaged);
        }
    }

    private static void ValidateSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, short.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, short.MaxValue);
    }

    private static IntPtr CreateEnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return IntPtr.Zero;
        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string)entry.Value!;
        }
        foreach (var (name, value) in overrides)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0') || value is not null && value.Contains('\0'))
            {
                throw new ArgumentException("Environment overrides require nonempty names without '=' or NUL and values without NUL.", nameof(overrides));
            }
            // Null takes the variable out of what the child inherits.
            if (value is null) environment.Remove(name);
            else environment[name] = value;
        }
        var block = string.Join('\0', environment.Select(entry => entry.Key + "=" + entry.Value)) + '\0';
        return Marshal.StringToHGlobalUni(block);
    }

    /// <summary>The output pipe's buffer, and the size of each read from it (<see cref="ReadOutput"/>).</summary>
    private const int OutputPipeSize = 64 * 1024;

    private void ReadOutput()
    {
        Exception? failure = null;
        try
        {
            var buffer = new byte[OutputPipeSize];
            int length;
            while ((length = _output.Read(buffer)) > 0)
            {
                _received = true;
                if (_discardOutput.IsCancellationRequested) continue;
                try
                {
                    _channel.Writer.WriteAsync(buffer[..length], _discardOutput.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (_discardOutput.IsCancellationRequested)
                {
                    // Keep draining the native pipe while a requested shutdown tears down ConPTY.
                }
            }
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 109 or 232)
        {
            // A broken/closing pipe is the normal ConPTY EOF signal.
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            _output.Dispose();
            _channel.Writer.TryComplete(failure);
        }
    }

    private async Task<int> ObserveExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            var exitCode = _process.ExitCode;
            var endedOnItsOwn = Volatile.Read(ref _disposed) == 0;
            CloseConsole();
            _job.Dispose();
            await _readTask.ConfigureAwait(false);
            // The reader has finished, so _received is final.
            if (_packaged && endedOnItsOwn && !_received) ConsoleHost.SilentHost(exitCode);
            return exitCode;
        }
        finally
        {
            _input.Dispose();
        }
    }

    private void CloseConsole()
    {
        IntPtr console;
        lock (_consoleGate)
        {
            console = _console;
            _console = IntPtr.Zero;
        }
        if (console != IntPtr.Zero) ClosePseudoConsole(console, _packaged);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _discardOutput.Cancel();
        try
        {
            await Task.Run(CloseConsole).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            _job.Dispose();
            _process.Dispose();
            _discardOutput.Dispose();
        }
    }
}
