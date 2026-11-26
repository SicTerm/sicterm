using System.Text;
using System.Threading.Channels;
using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ConPtySessionTests(Xunit.Abstractions.ITestOutputHelper testOutput)
{
    private static readonly string PowerShell = Environment.GetEnvironmentVariable("COMMAND_CENTRAL_PWSH")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");

    [Fact]
    public async Task Start_RunsInTheRequestedWorkingDirectory()
    {
        var directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? AppContext.BaseDirectory,
            "command-central-tests", Guid.NewGuid().ToString("N"), "cwd α & spaces");
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine([Environment]::CurrentDirectory)"],
                directory, 240, 30));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var capture = CaptureAsync(session.Output, timeout.Token);
            Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
            var actual = await capture;
            testOutput.WriteLine("[CONPTY-CWD] Requested: " + System.Text.Json.JsonSerializer.Serialize(directory));
            testOutput.WriteLine("[CONPTY-CWD] Full path: " + System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath(directory)));
            testOutput.WriteLine("[CONPTY-CWD] Output: " + System.Text.Json.JsonSerializer.Serialize(actual));
            Assert.Contains(directory, actual);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    [Fact]
    public async Task Start_UsesThePackagedConsoleHostShippedBesideTheApp()
    {
        Assert.Null(ConsoleHost.FallbackReason);
        Assert.Equal(ConsoleHost.Packaged, ConsoleHost.Current);
        Assert.StartsWith("1.", ConsoleHost.PackagedVersion);
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine('HOSTED')"], AppContext.BaseDirectory));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var capture = CaptureAsync(session.Output, timeout.Token);
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        Assert.Contains("HOSTED", await capture);
        Assert.Equal(ConsoleHost.Packaged, session.Host);
        Assert.Null(ConsoleHost.FallbackReason);
    }

    /// <summary>
    /// OpenConsole.exe is looked for where conpty.dll looks: beside it first, else in the folder for the machine's architecture
    /// (x64 here; arm64 for this x64 build on an ARM64 PC). Without it the packaged host is not used.
    /// </summary>
    [Fact]
    public void OpenConsolePath_IsBesideConptyOrInTheMachinesArchitectureFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sic-openconsole-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(ConsoleHost.OpenConsolePath(folder));
            foreach (var architecture in new[] { "x64", "arm64" })
            {
                Directory.CreateDirectory(Path.Combine(folder, architecture));
                File.WriteAllText(Path.Combine(folder, architecture, "OpenConsole.exe"), "");
            }
            var found = ConsoleHost.OpenConsolePath(folder);
            testOutput.WriteLine("[CONSOLE-HOST] OpenConsole.exe for this machine: " + found);
            Assert.Contains(found, new[] { Path.Combine(folder, "x64", "OpenConsole.exe"), Path.Combine(folder, "arm64", "OpenConsole.exe") });
            File.WriteAllText(Path.Combine(folder, "OpenConsole.exe"), "");
            Assert.Equal(Path.Combine(folder, "OpenConsole.exe"), ConsoleHost.OpenConsolePath(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The packaged host is started with PSEUDOCONSOLE_GLYPH_WIDTH_GRAPHEMES, so the cursor a program reads back counts columns
    /// as the renderer does: an accented letter written as a and a combining accent takes one column, an emoji two. Windows' own
    /// host gives the accent a column of its own (3 then 5 on build 26300, October 6, 2026), which is printed, not asserted.
    /// The packaged host also passes the text through as written, where Windows' own re-renders it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_CountsColumnsByGraphemeClusterInThePackagedHost(bool packaged)
    {
        const string script = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " +
            "[Console]::Write('a' + [char]0x301 + 'b'); $accent = [Console]::CursorLeft; " +
            "[Console]::Write([char]::ConvertFromUtf32(0x1F50C)); $emoji = [Console]::CursorLeft; " +
            "[Console]::WriteLine(); [Console]::WriteLine(('COLUMNS:{0}:{1}' -f $accent, $emoji))";
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], AppContext.BaseDirectory), preferPackaged: packaged);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var capture = CaptureAsync(session.Output, timeout.Token);
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        var output = await capture;
        var columns = System.Text.RegularExpressions.Regex.Match(output, @"COLUMNS:(\d+):(\d+)");
        testOutput.WriteLine($"[CONSOLE-HOST] {session.Host}: {columns.Value}; output {System.Text.Json.JsonSerializer.Serialize(output)}");
        Assert.True(columns.Success, "The child did not report its cursor columns.");
        Assert.Equal(packaged ? ConsoleHost.Packaged : ConsoleHost.Inbox, session.Host);
        if (!packaged) return;
        Assert.Equal(("2", "4"), (columns.Groups[1].Value, columns.Groups[2].Value));
        Assert.Contains("áb\U0001F50C", output);
    }

    /// <summary>
    /// A kitty graphics frame written the way the smoke fixture's PET verb and Hermes write one (an APC in two chunks, m=1 then
    /// m=0, then a row of placeholder cells in the image ID's colour), split as it arrives the way TerminalView splits it. The
    /// packaged host passes the image through and the splitter takes it out whole, leaving the text around it; Windows' own host
    /// drops it (docs/decisions/pet-images.md), which is printed, not asserted.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_PassesAKittyImageThroughThePackagedHost(bool packaged)
    {
        // A 16×16 PNG of one colour, as in MainWindow.Smoke.cs.
        const string png = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR42mP4sECBJMQwqmFUw/DVAAB52rAQuKcD2QAAAABJRU5ErkJggg==";
        var script = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $e = [char]27; $p = [char]::ConvertFromUtf32(0x10EEEE); " +
            "[Console]::Write('BEFORE' + $e + '_Ga=T,U=1,i=4660,c=4,r=2,f=100,q=2,m=1;" + png[..40] + "' + $e + '\\' + $e + '_Gm=0;" + png[40..] + "' + $e + '\\' + " +
            "'AFTER' + $e + '[38;2;0;18;52m' + $p + [char]0x305 + ($p * 3) + $e + '[39m'); [Console]::WriteLine()";
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], AppContext.BaseDirectory), preferPackaged: packaged);
        Assert.Equal(packaged ? ConsoleHost.Packaged : ConsoleHost.Inbox, session.Host);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var splitter = new KittyGraphicsSplitter();
        var commands = new List<KittyGraphicsCommand>();
        using var text = new MemoryStream();
        var reading = Task.Run(async () =>
        {
            await foreach (var chunk in session.Output.ReadAllAsync(timeout.Token)) text.Write(splitter.Split(chunk, commands));
        });
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        await reading;
        var output = Encoding.UTF8.GetString(text.ToArray());
        testOutput.WriteLine($"[KITTY] {session.Host}: {commands.Count} command(s); text {System.Text.Json.JsonSerializer.Serialize(output)}");
        if (!packaged) return;
        var command = Assert.Single(commands);
        Assert.Equal((4660u, 4, 2, png), (command.ImageId, command.Columns, command.Rows, command.Payload));
        Assert.Contains("BEFOREAFTER", output);
        Assert.Contains("\u001b[38;2;0;18;52m\U0010EEEE̅\U0010EEEE\U0010EEEE\U0010EEEE", output);
        Assert.DoesNotContain("_G", output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteInput_ReachesARealConsoleReadLine(bool packaged)
    {
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::WriteLine('READY'); $line = [Console]::ReadLine(); [Console]::WriteLine('ECHO:' + $line)"],
            AppContext.BaseDirectory), preferPackaged: packaged);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = CaptureAsync(session.Output, timeout.Token, "READY", ready);
        await ready.Task.WaitAsync(timeout.Token);
        await session.WriteInputAsync("hello α & spaces\r", timeout.Token);
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        Assert.Contains("ECHO:hello α & spaces", await capture);
    }

    /// <summary>Both hosts: a session resizes through the host that created its console.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resize_ChangesTheChildConsoleDimensions(bool packaged)
    {
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::WriteLine('READY'); $null = [Console]::ReadLine(); [Console]::WriteLine(('SIZE:{0}x{1}' -f [Console]::WindowWidth, [Console]::WindowHeight))"],
            AppContext.BaseDirectory), preferPackaged: packaged);
        Assert.Equal(packaged ? ConsoleHost.Packaged : ConsoleHost.Inbox, session.Host);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = CaptureAsync(session.Output, timeout.Token, "READY", ready);
        await ready.Task.WaitAsync(timeout.Token);
        session.Resize(96, 28);
        await session.WriteInputAsync("\r", timeout.Token);
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        Assert.Contains("SIZE:96x28", await capture);
    }

    [Fact]
    public async Task Dispose_StopsDetachedDescendantsOfThisSession()
    {
        const string script = "$start = [Diagnostics.ProcessStartInfo]::new(); " +
            "$start.FileName = [Environment]::ProcessPath; $start.UseShellExecute = $false; $start.CreateNoWindow = $true; " +
            "foreach ($arg in @('-NoLogo','-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 45')) { $start.ArgumentList.Add($arg) }; " +
            "$child = [Diagnostics.Process]::Start($start); [Console]::WriteLine(('CHILD:{0}:READY' -f $child.Id)); $null = [Console]::ReadLine()";
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], AppContext.BaseDirectory));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childId = 0;
        var capture = CaptureAsync(session.Output, timeout.Token, ":READY", ready, text =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"CHILD:(\d+):READY");
            if (match.Success) childId = int.Parse(match.Groups[1].Value);
        });
        await ready.Task.WaitAsync(timeout.Token);
        Assert.True(childId > 0);
        using var child = System.Diagnostics.Process.GetProcessById(childId);
        _ = child.SafeHandle;
        try
        {
            Assert.False(child.HasExited);
            await session.DisposeAsync();
            await Task.WhenAny(child.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(2), timeout.Token));
            Assert.True(child.HasExited, "A detached descendant survived its owning terminal's disposal.");
            _ = await capture;
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(timeout.Token);
            }
        }
    }

    [Fact]
    public async Task Start_OverridesOnlyTheChildEnvironment()
    {
        const string name = "COMMAND_CENTRAL_ENV_TEST";
        var original = Environment.GetEnvironmentVariable(name);
        var value = "child-" + Guid.NewGuid().ToString("N");
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine($env:COMMAND_CENTRAL_ENV_TEST)"],
            AppContext.BaseDirectory, Environment: new Dictionary<string, string?> { [name] = value }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var capture = CaptureAsync(session.Output, timeout.Token);
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        Assert.Contains(value, await capture);
        Assert.Equal(original, Environment.GetEnvironmentVariable(name));
    }

    [Fact]
    public async Task Start_LeavesOutAVariableWhoseOverrideIsNull()
    {
        const string name = "SIC_ENV_REMOVAL_TEST";
        Environment.SetEnvironmentVariable(name, "inherited");
        try
        {
            async Task<string> ChildSees(Dictionary<string, string?> environment)
            {
                await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine('present=' + (Test-Path Env:" + name + "))"],
                    AppContext.BaseDirectory, Environment: environment));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var capture = CaptureAsync(session.Output, timeout.Token);
                Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
                return await capture;
            }
            // Inherited while the overrides do not name it; gone once one names it with null.
            Assert.Contains("present=True", await ChildSees(new() { ["TERM"] = "xterm-256color" }));
            Assert.Contains("present=False", await ChildSees(new() { ["TERM"] = "xterm-256color", [name] = null }));
            Assert.Equal("inherited", Environment.GetEnvironmentVariable(name));
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }

    [Theory]
    [InlineData("dimensions")]
    [InlineData("relative-executable")]
    [InlineData("environment-name")]
    [InlineData("environment-nul")]
    public async Task Start_RejectsInvalidLaunchBoundaries(string invalidField)
    {
        var request = new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine('safe-test')"], AppContext.BaseDirectory);
        request = invalidField switch
        {
            "dimensions" => request with { Columns = 32768 },
            "relative-executable" => request with { Executable = "pwsh.exe" },
            "environment-name" => request with { Environment = new Dictionary<string, string?> { ["INVALID=NAME"] = "test" } },
            _ => request with { Environment = new Dictionary<string, string?> { ["COMMAND_CENTRAL_ENV_TEST"] = "test\0INJECTED=test" } }
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
        {
            await using var session = ConPtySession.Start(request);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var capture = CaptureAsync(session.Output, timeout.Token);
            await session.Completion.WaitAsync(timeout.Token);
            _ = await capture;
        });
    }

    /// <summary>
    /// The console host passes a program's OSC 7501 (Program Status Protocol) reports and its support query through to the
    /// terminal, whichever terminator the program uses: the agents' status reports reach the pane's xterm, so they can be read
    /// there instead of being guessed from titles and output. Checked here with the packaged host SicTerm ships.
    /// </summary>
    [Fact]
    public async Task Output_PassesProgramStatusReportsThrough()
    {
        const string command =
            "[Console]::Write([char]27 + ']7501;state=working:app=probe' + [char]27 + '\\');" +
            "[Console]::Write([char]27 + ']7501;state=done:app=probe' + [char]7);" +
            "[Console]::Write([char]27 + ']7501;?' + [char]27 + '\\');" +
            "[Console]::WriteLine('PROBE-END')";
        await using var session = ConPtySession.Start(new TerminalLaunchRequest(PowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command], AppContext.BaseDirectory));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var capture = CaptureAsync(session.Output, timeout.Token);
        Assert.Equal(0, await session.Completion.WaitAsync(timeout.Token));
        var actual = await capture;
        testOutput.WriteLine("[OSC-7501] Host: " + session.Host);
        testOutput.WriteLine("[OSC-7501] Output: " + System.Text.Json.JsonSerializer.Serialize(actual));
        Assert.Contains("PROBE-END", actual);
        Assert.Contains("\u001b]7501;state=working:app=probe", actual);
        Assert.Contains("\u001b]7501;state=done:app=probe", actual);
        Assert.Contains("\u001b]7501;?", actual);
    }

    private static async Task<string> CaptureAsync(ChannelReader<byte[]> output, CancellationToken cancellationToken,
        string? marker = null, TaskCompletionSource? ready = null, Action<string>? onMarker = null)
    {
        using var buffer = new MemoryStream();
        await foreach (var bytes in output.ReadAllAsync(cancellationToken))
        {
            buffer.Write(bytes);
            if (marker is not null && Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).Contains(marker))
            {
                onMarker?.Invoke(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
                ready?.TrySetResult();
            }
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
