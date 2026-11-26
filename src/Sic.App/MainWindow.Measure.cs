using System.Globalization;
using System.Text;
using System.Text.Json;
using Sic.Core.State;
using Sic.Core.Terminals;

namespace Sic.App;

/// <summary>
/// The hidden-page measurement behind the flow-control item (docs/decisions/performance-pass.md): how far a terminal's page falls
/// behind its output while its tab is not showing, and while the window is in the tray. A fixture prints numbered lines at a
/// steady rate into a pane, and a probe inside the page reads what it has received, what xterm has parsed, how often a chain of
/// 100 ms timers runs (the page's timer throttling, which xterm's parsing runs under) and whether the page is hidden. The pane's
/// tab is in view for half a minute, hidden behind another tab, shown again until it has caught up, then left selected while
/// the window is in the tray, and shown again. Run by scripts\Measure-HiddenOutput.ps1 (SicTerm.exe --measure-hidden-output),
/// with the smoke's isolation; it reports, it does not judge.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The fixture's lines: "SEQ:", an eight-digit number, "|", padding and CRLF, 64 bytes in all, short of any pane's width so none wraps.</summary>
    private const int MeasureLineBytes = 64;

    /// <summary>
    /// The fixture: PowerShell compiles a writer that prints the lines at {RATE} KB/s against a stopwatch, writes how many it has
    /// printed to {PROGRESS} twice a second, and stops when {STOP} appears. When {BURST} appears it prints {BURSTMB} MB of lines
    /// as fast as the pipe takes them, as a command printing a large file does, then goes on at its rate. Compiled, since a
    /// PowerShell loop does not keep a steady megabyte a second.
    /// </summary>
    private const string SteadyOutputScript = """
        Add-Type -TypeDefinition @'
        using System; using System.Diagnostics; using System.IO; using System.Text; using System.Threading;
        public static class SteadyOutput
        {
            public static void Run(int kilobytesPerSecond, string progress, string stop, string burst, int burstMegabytes)
            {
                var output = Console.OpenStandardOutput();
                var perSecond = Math.Max(1L, kilobytesPerSecond * 1024L / 64);
                var pad = new string('x', 64 - 4 - 8 - 1 - 2);
                var clock = Stopwatch.StartNew();
                long seq = 0, nextProgress = 0, burstLines = 0;
                var text = new StringBuilder(1 << 20);
                while (!File.Exists(stop))
                {
                    if (burstMegabytes > 0 && File.Exists(burst))
                    {
                        File.Delete(burst);
                        var lines = burstMegabytes * 1048576L / 64;
                        for (long written = 0; written < lines; )
                        {
                            text.Clear();
                            var chunk = Math.Min(16384L, lines - written);
                            for (long index = 0; index < chunk; index++) { text.Append("SEQ:").Append(seq.ToString("D8")).Append('|').Append(pad).Append("\r\n"); seq++; }
                            var chunkBytes = Encoding.ASCII.GetBytes(text.ToString());
                            output.Write(chunkBytes, 0, chunkBytes.Length);
                            output.Flush();
                            written += chunk;
                        }
                        burstLines += lines;
                    }
                    var target = (long)(clock.Elapsed.TotalSeconds * perSecond) + burstLines;
                    var behind = false;
                    if (target > seq)
                    {
                        text.Clear();
                        // At most a megabyte a write: a rate faster than the pipe takes leaves the target ahead, and the writer
                        // goes as fast as the pipe allows, without a pause, rather than building one ever larger string.
                        var limit = Math.Min(target, seq + 16384);
                        while (seq < limit) { text.Append("SEQ:").Append(seq.ToString("D8")).Append('|').Append(pad).Append("\r\n"); seq++; }
                        var bytes = Encoding.ASCII.GetBytes(text.ToString());
                        output.Write(bytes, 0, bytes.Length);
                        output.Flush();
                        behind = seq < target;
                    }
                    if (clock.ElapsedMilliseconds >= nextProgress)
                    {
                        File.WriteAllText(progress + ".tmp", seq + " " + clock.ElapsedMilliseconds);
                        File.Move(progress + ".tmp", progress, true);
                        nextProgress = clock.ElapsedMilliseconds + 500;
                    }
                    if (!behind) Thread.Sleep(20);
                }
            }
        }
        '@
        [SteadyOutput]::Run({RATE}, '{PROGRESS}', '{STOP}', '{BURST}', {BURSTMB})
        """;

    /// <summary>
    /// The probe, installed in the measured page once: a second listener on the host's messages counts the output bytes and
    /// reads the last line number in each message's tail; a chain of 100 ms timers counts its wake-ups and its longest gap;
    /// <c>window.__measure()</c> returns them with the last line number xterm has parsed (from the buffer) and the page's
    /// visibility, and starts the timer figures over.
    /// </summary>
    private const string MeasureProbeScript = """
        (() => {
          if (window.__measure) return 'ready';
          const m = { received: 0, messages: 0, receivedSeq: -1, wakeups: 0, maxGap: 0, last: performance.now() };
          const lastSeq = text => {
            let at = text.lastIndexOf('SEQ:');
            while (at >= 0 && !/^\d{8}$/.test(text.substr(at + 4, 8))) at = at > 0 ? text.lastIndexOf('SEQ:', at - 1) : -1;
            return at >= 0 ? parseInt(text.substr(at + 4, 8), 10) : -1;
          };
          window.chrome.webview.addEventListener('message', event => {
            const data = event.data;
            if (!data || data.type !== 'output' || typeof data.data !== 'string') return;
            const s = data.data;
            m.received += s.length / 4 * 3 - (s.endsWith('==') ? 2 : s.endsWith('=') ? 1 : 0);
            m.messages++;
            const seq = lastSeq(atob(s.length > 400 ? s.slice(s.length - 400) : s));
            if (seq >= 0) m.receivedSeq = seq;
          });
          const tick = () => { const now = performance.now(); m.maxGap = Math.max(m.maxGap, now - m.last); m.last = now; m.wakeups++; setTimeout(tick, 100); };
          setTimeout(tick, 100);
          window.__measure = () => {
            const result = { visibility: document.visibilityState, received: m.received, messages: m.messages, receivedSeq: m.receivedSeq,
              parsedSeq: lastSeq(window.sic.screenText()), wakeups: m.wakeups, maxGapMs: Math.round(m.maxGap),
              heapMB: performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : -1 };
            m.wakeups = 0; m.maxGap = 0;
            return result;
          };
          return 'installed';
        })()
        """;

    private async Task MeasureHiddenOutputAsync(OutputMeasure measure)
    {
        var data = _options.DataDirectory;
        var progress = Path.Combine(data, "steady-output.progress");
        var stop = Path.Combine(data, "steady-output.stop");
        var burst = Path.Combine(data, "steady-output.burst");
        var burstAt = -1.0;
        var linesPerSecond = Math.Max(1L, measure.Kilobytes * 1024L / MeasureLineBytes);
        var samples = new List<object>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string? browserVersion = null, error = null;
        long catchUpTab = -1, catchUpTray = -1;
        SessionPane? pane = null;
        try
        {
            browserVersion = (await TerminalView.EnvironmentAsync(Path.Combine(data, "WebView2"))).BrowserVersionString;
            var script = SteadyOutputScript.Replace("{RATE}", measure.Kilobytes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{PROGRESS}", progress.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
                .Replace("{STOP}", stop.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
                .Replace("{BURST}", burst.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
                .Replace("{BURSTMB}", measure.BurstMegabytes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var saved = new SavedPane(Guid.NewGuid().ToString("N"), "shell", "Steady output", data, "");
            var measured = CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), saved.Title, [saved]));
            var output = pane = measured.Panes[0];
            await output.StartAsync(CreateShellRequest(["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], data),
                new("the hidden-page measurement", DateTimeOffset.UtcNow));
            var terminal = output.Terminal!;
            await terminal.EvaluateForCheckAsync(MeasureProbeScript);

            async Task<JsonElement> ProbeAsync()
            {
                using var json = JsonDocument.Parse(await terminal.EvaluateForCheckAsync("window.__measure()"));
                return json.RootElement.Clone();
            }
            long Produced()
            {
                try { return long.Parse(File.ReadAllText(progress).Split(' ')[0], CultureInfo.InvariantCulture); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException) { return -1; }
            }
            async Task SampleAsync(string phase)
            {
                var produced = Produced();
                var probe = await ProbeAsync();
                var received = probe.GetProperty("receivedSeq").GetInt64();
                var parsed = probe.GetProperty("parsedSeq").GetInt64();
                var bytesPerLine = received > 0 ? probe.GetProperty("received").GetDouble() / (received + 1) : MeasureLineBytes;
                samples.Add(new
                {
                    phase, seconds = Math.Round(clock.Elapsed.TotalSeconds), visibility = probe.GetProperty("visibility").GetString(),
                    producedLines = produced, receivedLine = received, parsedLine = parsed,
                    // How far the screen is behind what the page has been handed, in time at the fixture's rate and in what xterm holds unparsed.
                    behindSeconds = Math.Round((received - parsed) / (double)linesPerSecond, 1),
                    pendingMB = Math.Round((received - parsed) * bytesPerLine / 1048576, 1),
                    // How far the page's messages are behind the fixture: the pipe, the session's channel and the hand-over.
                    deliveryBehindSeconds = produced < 0 ? (double?)null : Math.Round((produced - 1 - received) / (double)linesPerSecond, 1),
                    timerWakeups = probe.GetProperty("wakeups").GetInt32(), timerLongestGapMs = probe.GetProperty("maxGapMs").GetInt32(),
                    heapMB = probe.GetProperty("heapMB").GetInt32(), status = output.StatusText
                });
            }
            // Shown again: how long until the screen is within a quarter of a second of what the page has been handed.
            async Task<long> CatchUpAsync(string phase)
            {
                var started = clock.ElapsedMilliseconds;
                while (clock.ElapsedMilliseconds - started < 180_000)
                {
                    var probe = await ProbeAsync();
                    if (probe.GetProperty("receivedSeq").GetInt64() - probe.GetProperty("parsedSeq").GetInt64() <= linesPerSecond / 4)
                    {
                        var took = clock.ElapsedMilliseconds - started;
                        await SampleAsync(phase);
                        return took;
                    }
                    await Task.Delay(500);
                }
                await SampleAsync(phase + ", not caught up");
                return -1;
            }

            // The first lines, once PowerShell has compiled the writer.
            var firstBy = clock.Elapsed + TimeSpan.FromMinutes(1);
            while ((await ProbeAsync()).GetProperty("parsedSeq").GetInt64() < 0)
            {
                if (clock.Elapsed > firstBy) throw new TimeoutException("The fixture printed nothing within a minute.");
                await Task.Delay(250);
            }
            for (var index = 0; index < 6; index++) { await Task.Delay(5000); await SampleAsync("in view"); }
            // Another tab selected: the measured tab's content leaves the window, as every tab not showing does.
            CreateTab(new SavedTab(Guid.NewGuid().ToString("N"), "Another tab", [new SavedPane(Guid.NewGuid().ToString("N"), "shell", "Another tab", data, "")]));
            var hiddenAt = clock.Elapsed;
            var until = hiddenAt + TimeSpan.FromMinutes(measure.HiddenMinutes);
            while (clock.Elapsed < until && output.Status != PaneStatus.Failed)
            {
                await Task.Delay(10_000);
                // The burst, once, a set time after the tab was hidden: a command printing a large file while nobody looks.
                if (measure.BurstMegabytes > 0 && burstAt < 0 && clock.Elapsed - hiddenAt >= TimeSpan.FromSeconds(measure.BurstAfterSeconds))
                {
                    File.WriteAllText(burst, "");
                    burstAt = Math.Round(clock.Elapsed.TotalSeconds);
                }
                await SampleAsync(burstAt < 0 ? "tab hidden" : "tab hidden, after the burst");
            }
            _tabs.SelectedItem = measured.Item;
            if (output.Status != PaneStatus.Failed) catchUpTab = await CatchUpAsync("tab shown again");
            // The window in the tray with the measured tab selected, as Close and minimize's tray leaves it.
            if (measure.TrayMinutes > 0 && output.Status != PaneStatus.Failed)
            {
                AppWindow.Hide();
                until = clock.Elapsed + TimeSpan.FromMinutes(measure.TrayMinutes);
                while (clock.Elapsed < until && output.Status != PaneStatus.Failed) { await Task.Delay(10_000); await SampleAsync("in the tray"); }
                ShowFromTray();
                if (output.Status != PaneStatus.Failed) catchUpTray = await CatchUpAsync("window shown again");
            }
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            Environment.ExitCode = 1;
        }
        finally
        {
            try { File.WriteAllText(stop, ""); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            _options.WriteReport(new
            {
                completed = error is null, error, paneFailed = pane?.Status == PaneStatus.Failed, paneStatus = pane?.StatusText,
                rateKilobytes = measure.Kilobytes, linesPerSecond, hiddenMinutes = measure.HiddenMinutes, trayMinutes = measure.TrayMinutes,
                burstMegabytes = measure.BurstMegabytes, burstAtSeconds = burstAt,
                browserVersion, browserArguments = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? "", consoleHost = ConsoleHost.Current,
                catchUpAfterTabMs = catchUpTab, catchUpAfterTrayMs = catchUpTray, samples
            });
            await CloseAsync(force: true);
        }
    }
}
