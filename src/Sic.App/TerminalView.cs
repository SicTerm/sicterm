using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Sic.Core.Appearance;
using Sic.Core.State;
using Sic.Core.Terminals;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Sic.App;

/// <summary>A right-click: whether there is a terminal selection, whether the program has mouse reporting on, and whether it copied (OSC 52) since the last click.</summary>
internal sealed record TerminalContextMenuRequest(double X, double Y, bool HasSelection, bool MouseReporting = false, bool ProgramCopied = false);

/// <summary>One xterm.js renderer in a WebView2 bound to one ConPTY session. The host draws every piece of chrome; the page draws only cells.</summary>
internal sealed class TerminalView : Grid, IAsyncDisposable
{
    private const string Host = "appassets.sic.local";
    private const string PageBase = "https://appassets.sic.local/terminal.html";
    private static readonly TerminalPalette Palette = ThemeCatalog.Terminal;
    /// <summary>The page's colors (<see cref="RendererTheme"/>), made once: the palette never changes.</summary>
    private static readonly object PageTheme = RendererTheme(Palette);
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly WebView2 _webView = new() { Opacity = 0 };
    private string? _page;
    private readonly string _browserDirectory;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Set when the view first joins the window, cancelled if it is disposed before then; see WaitForPageAsync.
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConPtySession? _session;
    private Task? _initialization;
    private Task? _outputPump;
    private int _columns = 120, _rows = 30;
    private bool _disposed;
    private Task? _disposal;
    private PaneNeighbors _neighbors;
    private bool _dropOutlined;
    // Settings → Pet images: whether kitty graphics leave the output for the page (docs/decisions/pet-images.md). Read by the
    // output pump on its own thread, set on the UI thread.
    private volatile bool _petImages = true;
    private readonly KittyImagePoster _images = new();

    internal Task Ready => _ready.Task;
    /// <summary>How long the terminal page may take to load once the view is in the window. Verification shortens it.</summary>
    internal static TimeSpan LoadBudget { get; set; } = TimeSpan.FromSeconds(25);
    internal bool IsRunning => _session is { Completion.IsCompleted: false };
    /// <summary>The process the terminal runs and when it started; null until it has started.</summary>
    internal int? ProcessId => _session?.ProcessId;
    internal DateTimeOffset? ProcessStartedAt { get; private set; }
    internal event Action<Exception>? Failed;
    internal event Action<int>? Exited;
    /// <summary>The terminal showed output, the latest that long ago: the page posts at most one report a burst, at its start and once at its end.</summary>
    internal event Action<TimeSpan>? Activity;
    internal event Action? Bell;
    internal event Action<string>? TitleChanged;
    /// <summary>The program reported its state over OSC 7501 (docs/decisions/program-status.md): the report's body as sent, for <see cref="Sic.Core.Agents.ProgramStatus.Parse"/>.</summary>
    internal event Action<string>? StatusReported;
    /// <summary>What the page sends the process: keystrokes and pastes, and xterm's own answers to the process's queries.</summary>
    internal event Action<string>? Input;
    internal event Action<TerminalContextMenuRequest>? ContextMenuRequested;
    internal event Action<string, string?>? Shortcut;
    /// <summary>
    /// The program put text on the clipboard (OSC 52), this long after the user's last key, click or drag in the terminal (null
    /// when there was none); the page takes writes only, never a read.
    /// </summary>
    internal event Action<string, TimeSpan?>? ProgramCopied;
    /// <summary>
    /// Files, folders or text dropped on the terminal, as the text to paste: the paths quoted for PowerShell, or the text. The
    /// pane pastes it as it pastes the clipboard.
    /// </summary>
    internal event Action<string>? Dropped;
    /// <summary>The program asked to put this many characters on the clipboard, more than one message carries; nothing was copied.</summary>
    internal event Action<int>? ProgramCopyRefused;
    internal event Action<int, int>? SearchResults;
    /// <summary>
    /// The drag caption for files and folders. SIC only types their paths, but what the program does with them is its own
    /// affair: Claude Code attaches a dropped image, so an agent pane says Add to prompt where PowerShell says Paste path.
    /// </summary>
    internal string FileDropCaption { get; init; } = "Paste path";

    /// <summary>
    /// Settings → Pet images as the view starts; a change later comes with the appearance (<see cref="ApplyAppearanceAsync"/>).
    /// Off, the output reaches the page untouched, as before images were drawn, and the page draws none.
    /// </summary>
    internal bool PetImages { get => _petImages; init => _petImages = value; }

    /// <summary>
    /// The font and size the page opens with (Settings → Terminal), carried in the page's address so the terminal is built in
    /// them: the grid the page reports ready with is then the pane's true grid, the pseudoconsole starts at it, and the
    /// appearance sent after the start changes nothing. They used to reach the page only then, so a font or size other than
    /// the page's own made every start resize the pseudoconsole and the agent repaint its first screen.
    /// </summary>
    internal string FontFamily { get; init; } = "Cascadia Mono, Consolas, monospace";
    internal int FontSize { get; init; } = 14;

    /// <summary>The page's address, with the colors, the font and the size it opens with; what every navigation and message is checked against.</summary>
    private string Page => _page ??= PageBase + "?bg=" + Palette.Background.ToLowerInvariant() + "&fg=" + Palette.Foreground.ToLowerInvariant()
        + "&font=" + Uri.EscapeDataString(FontFamily) + "&size=" + FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The sides the page takes Alt+Arrow toward: those another pane of its tab lies on, and up, where the top row has the tab
    /// strip above it. The rest reach the terminal. The page decides as the key comes in, so the sides are sent whenever they
    /// change and again once the page is ready.
    /// </summary>
    internal PaneNeighbors Neighbors
    {
        get => _neighbors;
        set
        {
            if (_neighbors == value) return;
            _neighbors = value;
            SendNeighbors();
        }
    }

    /// <summary>The terminal's colors are <see cref="ThemeCatalog.Terminal"/> whatever the app theme; see that property for why.</summary>
    internal TerminalView(string browserDirectory)
    {
        _browserDirectory = browserDirectory;
        var background = Theme.Parse(Palette.Background);
        Background = new SolidColorBrush(background);
        _webView.DefaultBackgroundColor = background;
        Children.Add(_webView);
        // Files, folders and text dragged from another app. Under WinUI the WebView2 does not hand such a drag to the page (seen
        // October 4, 2026: a not-allowed cursor), so the view takes it here and pastes as the page's own drop would.
        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += (_, _) => OutlineDrop(false);
        Drop += OnDrop;
        Loaded += async (_, _) =>
        {
            if (_initialization is not null || _disposal is not null) return;
            _loaded.TrySetResult();
            _initialization = InitializeAsync();
            try { await _initialization; }
            catch (Exception exception) { _ready.TrySetException(exception); Failed?.Invoke(exception); }
        };
    }

    /// <summary>
    /// One browser environment per browser folder, shared by every terminal and made once: each view used to make its own,
    /// which found the runtime and opened the user data folder again for every pane. The first use also brings up the shared
    /// browser process, so <see cref="WarmUp"/> asks for it at launch, off the critical path, before any terminal needs it.
    /// Used on the UI thread only. A creation that fails is forgotten, so the next terminal tries again rather than failing on the same error.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<CoreWebView2Environment>> Environments = new(StringComparer.OrdinalIgnoreCase);

    internal static Task<CoreWebView2Environment> EnvironmentAsync(string browserDirectory) => Environments.GetOrAdd(browserDirectory, CreateEnvironmentAsync);

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync(string browserDirectory)
    {
        try { return await CoreWebView2Environment.CreateWithOptionsAsync(null, browserDirectory, null); }
        catch { Environments.TryRemove(browserDirectory, out _); throw; }
    }

    /// <summary>Starts the browser environment ahead of the first terminal; nothing waits for it, and a failure is the first terminal's to report.</summary>
    internal static void WarmUp(string browserDirectory)
    {
        _ = EnvironmentAsync(browserDirectory).ContinueWith(task => Diagnostics.Log("browser warm-up: " + task.Exception?.GetBaseException().Message),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private async Task InitializeAsync()
    {
        var environment = await EnvironmentAsync(_browserDirectory);
        await _webView.EnsureCoreWebView2Async(environment);
        var core = _webView.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Assets", "terminal"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, args) => { if (!string.Equals(args.Uri, Page, StringComparison.OrdinalIgnoreCase)) args.Cancel = true; };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess) return;
            var exception = new InvalidOperationException("The terminal page failed to load: " + args.WebErrorStatus);
            _ready.TrySetException(exception);
            Failed?.Invoke(exception);
        };
        core.WebMessageReceived += HandleMessage;
        core.Navigate(Page);
    }

    private async void HandleMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (!string.Equals(args.Source, Page, StringComparison.OrdinalIgnoreCase)) return;
            // Read once: each read marshals the text from the browser, and a keystroke and an output report come this way.
            var json = args.WebMessageAsJson;
            if (json.Length > 1_048_576) return;
            using var document = JsonDocument.Parse(json);
            var message = document.RootElement;
            var type = message.GetProperty("type").GetString();
            switch (type)
            {
                case "ready":
                    _columns = message.GetProperty("columns").GetInt32();
                    _rows = message.GetProperty("rows").GetInt32();
                    _webView.Opacity = 1;
                    _ready.TrySetResult();
                    SendNeighbors();
                    break;
                case "renderer-error":
                    var rendererError = new InvalidOperationException("Terminal renderer: " + message.GetProperty("message").GetString());
                    _ready.TrySetException(rendererError);
                    Failed?.Invoke(rendererError);
                    break;
                case "input" when IsRunning:
                    var input = message.GetProperty("data").GetString() ?? "";
                    Input?.Invoke(input);
                    await _session!.WriteInputAsync(input);
                    break;
                case "resize":
                    _columns = message.GetProperty("columns").GetInt32();
                    _rows = message.GetProperty("rows").GetInt32();
                    if (IsRunning && _columns > 0 && _rows > 0) _session!.Resize(_columns, _rows);
                    break;
                case "link":
                    if (Uri.TryCreate(message.GetProperty("uri").GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    break;
                case "activity":
                    Activity?.Invoke(TimeSpan.FromMilliseconds(message.TryGetProperty("age", out var age) && age.TryGetDouble(out var ago) ? Math.Max(0, ago) : 0));
                    break;
                case "bell": Bell?.Invoke(); break;
                case "title": TitleChanged?.Invoke(message.GetProperty("title").GetString() ?? ""); break;
                case "program-status":
                    if (message.GetProperty("body").GetString() is { Length: > 0 and <= Sic.Core.Agents.ProgramStatus.BodyLimit } body) StatusReported?.Invoke(body);
                    break;
                case "contextmenu":
                    ContextMenuRequested?.Invoke(new TerminalContextMenuRequest(message.GetProperty("x").GetDouble(), message.GetProperty("y").GetDouble(),
                        message.TryGetProperty("hasSelection", out var selected) && selected.ValueKind == JsonValueKind.True,
                        message.TryGetProperty("mouseReporting", out var reporting) && reporting.ValueKind == JsonValueKind.True,
                        message.TryGetProperty("programCopied", out var programCopied) && programCopied.ValueKind == JsonValueKind.True));
                    break;
                case "program-copy":
                    if (message.GetProperty("text").GetString() is { Length: > 0 } copied)
                        // Null when the user has not pressed a key or a button in the pane; TryGetDouble throws on anything but a number.
                        ProgramCopied?.Invoke(copied, message.TryGetProperty("sinceGesture", out var since) && since.ValueKind == JsonValueKind.Number
                            && since.TryGetDouble(out var milliseconds) && milliseconds >= 0 ? TimeSpan.FromMilliseconds(milliseconds) : null);
                    break;
                case "program-copy-refused":
                    ProgramCopyRefused?.Invoke(message.TryGetProperty("length", out var length) && length.TryGetInt32(out var characters) ? characters : 0);
                    break;
                case "shortcut":
                    Shortcut?.Invoke(message.GetProperty("name").GetString() ?? "", message.TryGetProperty("text", out var text) ? text.GetString() : null);
                    break;
                case "search-results":
                    SearchResults?.Invoke(message.GetProperty("index").GetInt32(), message.GetProperty("count").GetInt32());
                    break;
                case "kitty-miss" when _petImages:
                    // The page was sent a frame by reference and no longer holds it: the image goes again in full, if still remembered.
                    if (message.TryGetProperty("key", out var key) && key.TryGetInt64(out var missing) && _images.Resend(missing) is { } resent)
                        _webView.CoreWebView2.PostWebMessageAsJson(resent);
                    break;
                case "drop" when IsRunning:
                    // Files and folders from Explorer arrive as File objects whose full paths only the host can read; text arrives as text.
                    PasteDrop(args.AdditionalObjects is { Count: > 0 } files
                        ? PathPaste.Join(files.OfType<CoreWebView2File>().Select(file => file.Path))
                        : PathPaste.FromText(message.TryGetProperty("text", out var droppedText) ? droppedText.GetString() ?? "" : ""));
                    break;
            }
        }
        catch (ObjectDisposedException) when (!IsRunning) { }
        catch (Exception exception) { Failed?.Invoke(exception); }
    }

    /// <summary>
    /// Tells a ready page its sides; a page still loading hears them when it reports ready. A page whose browser has gone
    /// reports that through its own output and failure paths, and a split or close must not fail over it.
    /// </summary>
    private void SendNeighbors()
    {
        if (!Ready.IsCompletedSuccessfully || _disposal is not null) return;
        var message = JsonSerializer.Serialize(new { type = "neighbors", left = _neighbors.Left, right = _neighbors.Right, up = _neighbors.Up, down = _neighbors.Down });
        try { _webView.CoreWebView2.PostWebMessageAsJson(message); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
    }

    /// <summary>
    /// Waits for the terminal page, counting only the time since the view joined the window. A TabView holds only the
    /// selected tab's content, so a view made for a pane in another tab is not loaded and its page cannot start; the budget
    /// used to run from the moment the view was made, and a start whose tab was not showing then failed with "The operation
    /// has timed out." unless the tab was shown in time. It now waits for the tab, without a limit. Disposing the view ends
    /// the wait with a cancellation, whichever part of it is under way.
    /// </summary>
    private async Task WaitForPageAsync()
    {
        await _loaded.Task;
        try { await Ready.WaitAsync(LoadBudget); }
        catch (TimeoutException) { throw new TimeoutException("The terminal page did not load within " + StartConflict.Elapsed(LoadBudget) + "."); }
    }

    /// <summary>
    /// Starts <paramref name="request"/> once the page is ready. <paramref name="mayStart"/> is asked again at that moment,
    /// since the wait can be long: a close decided meanwhile (a tab, a pane, a Quit) has already asked the running agents to
    /// exit, and one started after that would be stopped by the teardown without being asked.
    /// </summary>
    internal async Task StartAsync(TerminalLaunchRequest request, Func<bool>? mayStart = null)
    {
        await WaitForPageAsync();
        // The page can report ready and the view be closed before this continuation runs; nothing may start in a closed view.
        if (_disposal is not null || mayStart?.Invoke() == false) throw new OperationCanceledException("The terminal was closed before it started.");
        if (IsRunning) throw new InvalidOperationException("This pane already owns a running terminal.");
        // The terminal inherits SicTerm's variables: brought in line first with any an installer changed since SicTerm started.
        EnvironmentRefresh.Apply();
        _session = ConPtySession.Start(request with { Columns = _columns, Rows = _rows });
        ProcessStartedAt = DateTimeOffset.UtcNow;
        SendPty(_session.Host);
        // The settings follow the start (SessionPane); this one must be in place before the first output, or with pet images
        // off the page would paint over a pet's placeholder cells until they came.
        _webView.CoreWebView2.PostWebMessageAsJson(_petImages ? """{"type":"pet-images","on":true}""" : """{"type":"pet-images","on":false}""");
        _outputPump = PumpOutputAsync(_session);
        await FocusInputAsync();
    }

    /// <summary>The console host the terminal's pseudoconsole runs in (<see cref="ConsoleHost"/>), null before it starts.</summary>
    internal string? PseudoconsoleHost => _session?.Host;

    /// <summary>
    /// Tells the page which console host it draws for, ahead of the host's first output (xterm's windowsPty; see terminal.ts).
    /// The packaged host reflows on any Windows, so it is never given a build below 21376, where xterm starts reflowing a ConPTY;
    /// Windows' own is given the Windows build it is.
    /// </summary>
    private void SendPty(string host)
    {
        var build = Environment.OSVersion.Version.Build;
        var buildNumber = host == ConsoleHost.Packaged ? Math.Max(build, 21376) : build;
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "pty", buildNumber }));
    }

    /// <summary>
    /// The most output one hand-over to the page carries. An agent redrawing its whole screen writes hundreds of kilobytes a
    /// second; each hand-over is a trip through the UI thread and the browser's message channel, so what arrives while the
    /// page takes one goes in the next as a single message, up to this much, rather than a message per pipe read.
    /// </summary>
    private const int OutputBatchLimit = 128 * 1024;

    private async Task PumpOutputAsync(ConPtySession session)
    {
        // Kitty graphics (Hermes's pet) leave the stream here while Settings → Pet images is on, since xterm swallows APC
        // unread; see docs/decisions/pet-images.md. Off, there is no splitter and the output goes through untouched.
        KittyGraphicsSplitter? graphics = null;
        var commands = new List<KittyGraphicsCommand>();
        // One hand-over's messages: the text in as few output messages as the images in it allow, in the order they came.
        var messages = new List<string>();
        var text = new ArrayBufferWriter<byte>(16384);
        void FlushText()
        {
            if (text.WrittenCount == 0) return;
            messages.Add(OutputMessage(text.WrittenMemory));
            text.ResetWrittenCount();
        }
        var droppedLogged = false;
        try
        {
            var output = session.Output;
            while (await output.WaitToReadAsync().ConfigureAwait(false))
            {
                var taken = 0;
                while (taken < OutputBatchLimit && output.TryRead(out var chunk))
                {
                    taken += chunk.Length;
                    var bytes = chunk;
                    if (_petImages) graphics ??= new KittyGraphicsSplitter();
                    if (graphics is not null)
                    {
                        bytes = graphics.Split(chunk, commands);
                        foreach (var command in commands)
                            if (_petImages && _images.ToPageMessage(command) is { } image) { FlushText(); messages.Add(image); }
                        commands.Clear();
                        // Turned off: once no command is part-read, the output goes on without the splitter.
                        if (!_petImages && graphics.Idle) graphics = null;
                    }
                    if (bytes.Length > 0) text.Write(bytes);
                }
                FlushText();
                if (messages.Count == 0) continue;
                var batch = messages.ToArray();
                messages.Clear();
                // The control gives up its CoreWebView2 when it is closed, or unloaded from the tree, while output is still coming (seen
                // October 7, 2026, as a null reference from here); output with nowhere to go is dropped rather than failing the pane,
                // and the first such drop of a view not being closed is logged, since a tab's output would be lost rather than late.
                await DispatchAsync(() =>
                {
                    if (_webView.CoreWebView2 is { } core) { foreach (var message in batch) core.PostWebMessageAsJson(message); return; }
                    if (droppedLogged || _disposal is not null) return;
                    droppedLogged = true;
                    Diagnostics.Log("terminal: output dropped, the view has no browser (process " + session.ProcessId + ")");
                }).ConfigureAwait(false);
            }
            var exitCode = await session.Completion.ConfigureAwait(false);
            await DispatchAsync(() => Exited?.Invoke(exitCode)).ConfigureAwait(false);
        }
        catch (Exception exception) when (!_disposed)
        {
            await DispatchAsync(() => Failed?.Invoke(exception)).ConfigureAwait(false);
        }
    }

    private const string OutputPrefix = "{\"type\":\"output\",\"data\":\"", OutputSuffix = "\"}";

    /// <summary>
    /// The page's output message, written by hand into one string of the exact length: base64 needs no escaping, the serializer's
    /// reflection is spared on the hottest path, and a batch near the limit no longer makes the base64 and then the message
    /// again, two large-object strings a hand-over while the page is behind.
    /// </summary>
    private static string OutputMessage(ReadOnlyMemory<byte> bytes) =>
        string.Create(OutputPrefix.Length + (bytes.Length + 2) / 3 * 4 + OutputSuffix.Length, bytes, static (span, state) =>
        {
            OutputPrefix.CopyTo(span);
            Convert.TryToBase64Chars(state.Span, span[OutputPrefix.Length..], out var written);
            OutputSuffix.CopyTo(span[(OutputPrefix.Length + written)..]);
        });

    private Task DispatchAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) { action(); return Task.CompletedTask; }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { action(); completed.TrySetResult(); }
            catch (Exception exception) { completed.TrySetException(exception); }
        })) completed.TrySetException(new ObjectDisposedException(nameof(TerminalView)));
        return completed.Task;
    }

    private static string Css(string hex) => "#" + hex.ToLowerInvariant();

    private static object RendererTheme(TerminalPalette palette) => new
    {
        background = Css(palette.Background), foreground = Css(palette.Foreground), cursor = Css(palette.Cursor), selectionBackground = Css(palette.Selection),
        black = Css(palette.Black), red = Css(palette.Red), green = Css(palette.Green), yellow = Css(palette.Yellow),
        blue = Css(palette.Blue), magenta = Css(palette.Magenta), cyan = Css(palette.Cyan), white = Css(palette.White),
        brightBlack = Css(palette.BrightBlack), brightRed = Css(palette.BrightRed), brightGreen = Css(palette.BrightGreen), brightYellow = Css(palette.BrightYellow),
        brightBlue = Css(palette.BrightBlue), brightMagenta = Css(palette.BrightMagenta), brightCyan = Css(palette.BrightCyan), brightWhite = Css(palette.BrightWhite)
    };

    /// <summary>
    /// The font and the terminal options from Settings, with the fixed palette. The app theme plays no part. Pet images turned
    /// off stop the pump posting images before the page drops its own; turned on, the pump posts again only once the page takes them.
    /// </summary>
    internal async Task ApplyAppearanceAsync(AppPreferences preferences)
    {
        await WaitForPageAsync();
        var accent = Css(ThemeCatalog.TerminalAccent);
        var settings = new
        {
            theme = PageTheme, preferences.FontFamily, preferences.FontSize, searchMatch = Css(Palette.Selection), searchActive = accent, accent,
            cursorStyle = TerminalOptions.NormalizeCursorStyle(preferences.CursorStyle), preferences.CursorBlink,
            scrollback = TerminalOptions.ClampScrollback(preferences.Scrollback), preferences.CopyOnSelect, preferences.PetImages
        };
        if (!preferences.PetImages && _petImages)
        {
            _petImages = false;
            _images.Clear();
        }
        await _webView.CoreWebView2.ExecuteScriptAsync("window.sic.setAppearance(" + JsonSerializer.Serialize(settings, CamelCase) + ")");
        if (preferences.PetImages) _petImages = true;
    }

    private async Task<string> ScriptAsync(string expression)
    {
        await WaitForPageAsync();
        return await _webView.CoreWebView2.ExecuteScriptAsync(expression);
    }

    internal async Task<(string Background, string Foreground)> ReadAppearanceAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.appearance()"));
        return (json.RootElement.GetProperty("background").GetString() ?? "", json.RootElement.GetProperty("foreground").GetString() ?? "");
    }
    /// <summary>The cursor and scrollback options the renderer is actually using, for verification.</summary>
    internal async Task<(string CursorStyle, string CursorInactiveStyle, bool CursorBlink, int Scrollback, bool CopyOnSelect)> ReadOptionsAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.options()"));
        var options = json.RootElement;
        return (options.GetProperty("cursorStyle").GetString() ?? "", options.GetProperty("cursorInactiveStyle").GetString() ?? "",
            options.GetProperty("cursorBlink").GetBoolean(), options.GetProperty("scrollback").GetInt32(), options.GetProperty("copyOnSelect").GetBoolean());
    }
    /// <summary>
    /// The cursor as the renderer draws it, for verification: whether the terminal holds focus, the cursor cell's classes, and
    /// the CSS animation left on it (none unless it blinks).
    /// </summary>
    internal async Task<(bool Focused, string Classes, string Animation, bool ReducedMotion)> ReadCursorAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.cursor()"));
        var cursor = json.RootElement;
        return (cursor.GetProperty("focused").GetBoolean(), cursor.GetProperty("classes").GetString() ?? "", cursor.GetProperty("animation").GetString() ?? "",
            cursor.GetProperty("reducedMotion").GetBoolean());
    }
    /// <summary>
    /// Verification: tells the page that Windows asks for reduced motion (true) or does not (false), or hands the answer back
    /// to Windows (null), so the reduced-motion rules can be checked on a machine whatever its own animation setting.
    /// </summary>
    internal async Task EmulateReducedMotionAsync(bool? reduce)
    {
        await WaitForPageAsync();
        var features = reduce is { } on ? JsonSerializer.Serialize(new[] { new { name = "prefers-reduced-motion", value = on ? "reduce" : "no-preference" } }) : "[]";
        await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":" + features + "}");
    }
    /// <summary>Verification: renders text as if the program had printed it, for control sequences the fixture cannot send.</summary>
    internal async Task RenderAsync(string text) => await ScriptAsync("window.sic.render(" + JsonSerializer.Serialize(text) + ")");
    /// <summary>
    /// Verification: selects the last screen line containing <paramref name="text"/> and releases the mouse over the terminal,
    /// the way a drag ends. Returns the text the page sent to be copied, empty when it sent nothing.
    /// </summary>
    internal async Task<string> SelectAndReleaseAsync(string text) =>
        JsonSerializer.Deserialize<string>(await ScriptAsync("window.sic.selectAndRelease(" + JsonSerializer.Serialize(text) + ")")) ?? "";
    /// <summary>Verification: whether the program has copied (OSC 52) since the last press of the left button, which is what lets the menu's Copy take it.</summary>
    internal async Task<bool> ProgramCopiedAsync() => JsonSerializer.Deserialize<bool>(await ScriptAsync("window.sic.programCopied()"));
    /// <summary>Verification: a press of the left button sent to the page's window, where its own listeners hear it and xterm's do not.</summary>
    internal async Task PressLeftAsync() => await ScriptAsync("window.dispatchEvent(new MouseEvent('mousedown', { button: 0 }))");
    /// <summary>Verification: the page forgets the user's last key or click, so a program's copy that follows is one nobody asked for.</summary>
    internal async Task ForgetGestureAsync() => await ScriptAsync("window.sic.forgetGesture()");
    /// <summary>
    /// Verification: a paste event carrying <paramref name="text"/> on xterm's textarea, as the browser fires one for a paste it
    /// makes itself. True when the page stopped it before xterm pasted the text; the host then pastes the clipboard.
    /// </summary>
    internal async Task<bool> PasteEventAsync(string text) => JsonSerializer.Deserialize<bool>(await ScriptAsync("window.sic.pasteEvent(" + JsonSerializer.Serialize(text) + ")"));
    internal async Task<double> ReadLineHeightAsync() => JsonSerializer.Deserialize<double>(await ScriptAsync("window.sic.lineHeight()"));

    /// <summary>The font the terminal draws with, whether its faces have loaded, and an M's width in it, for verification.</summary>
    internal async Task<(string Family, bool Loaded, double CellWidth)> ReadFontAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.font()"));
        var root = json.RootElement;
        return (root.GetProperty("family").GetString() ?? "", root.GetProperty("loaded").GetBoolean(), root.GetProperty("cellWidth").GetDouble());
    }
    internal async Task SendFromRendererAsync(string text) => await ScriptAsync("window.sic.input(" + JsonSerializer.Serialize(text) + ")");
    internal async Task<bool> PressKeyAsync(string key, bool ctrl = false, bool shift = false, bool alt = false, string code = "") =>
        JsonSerializer.Deserialize<bool>(await ScriptAsync("window.sic.key(" + JsonSerializer.Serialize(key) + "," + JsonSerializer.Serialize(new { ctrlKey = ctrl, shiftKey = shift, altKey = alt, code }) + ")"));
    /// <summary>The sides the page takes Alt+Arrow toward, as it last heard them, for verification.</summary>
    internal async Task<PaneNeighbors> ReadNeighborsAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.neighbors()"));
        var sides = json.RootElement;
        return new(sides.GetProperty("left").GetBoolean(), sides.GetProperty("right").GetBoolean(), sides.GetProperty("up").GetBoolean(), sides.GetProperty("down").GetBoolean());
    }
    /// <summary>Whether the window's keyboard focus is on this terminal, for verification.</summary>
    internal bool HasFocus => _webView.FocusState != FocusState.Unfocused;
    private static bool Droppable(DataPackageView data) => data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Text);

    private void OnDragOver(object sender, DragEventArgs args)
    {
        if (!IsRunning || !Droppable(args.DataView)) { args.AcceptedOperation = DataPackageOperation.None; return; }
        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = args.DataView.Contains(StandardDataFormats.StorageItems) ? FileDropCaption : "Paste";
        OutlineDrop(true);
    }

    private async void OnDrop(object sender, DragEventArgs args)
    {
        OutlineDrop(false);
        if (!IsRunning || !Droppable(args.DataView)) return;
        var deferral = args.GetDeferral();
        try
        {
            PasteDrop(args.DataView.Contains(StandardDataFormats.StorageItems)
                ? PathPaste.Join((await args.DataView.GetStorageItemsAsync()).Select(item => item.Path))
                : PathPaste.FromText(await args.DataView.GetTextAsync()));
        }
        catch (Exception exception) { Failed?.Invoke(exception); }
        finally { deferral.Complete(); }
    }

    /// <summary>Lights or clears the page's accent outline, once per change, for a drag the page itself never sees.</summary>
    private async void OutlineDrop(bool on)
    {
        if (_dropOutlined == on) return;
        _dropOutlined = on;
        // Only the outline: a page that is gone or not ready is no reason to fail the terminal.
        try { await ScriptAsync("window.sic.outlineDrop(" + (on ? "true" : "false") + ")"); }
        catch (Exception) { }
    }

    /// <summary>Hands dropped paths or text to the pane, which pastes them as it pastes the clipboard (<see cref="Dropped"/>).</summary>
    private void PasteDrop(string dropped)
    {
        if (dropped.Length > 0) Dropped?.Invoke(dropped);
    }

    /// <summary>
    /// Types text as a paste. Every paste reaches the page through here, so the filter is applied here whoever calls: control
    /// characters out but the tab, each line break one Enter (<see cref="ClipboardText.ForPaste"/>), as Windows Terminal pastes.
    /// </summary>
    internal async Task PasteAsync(string text) => await ScriptAsync("window.sic.paste(" + JsonSerializer.Serialize(ClipboardText.ForPaste(text)) + ")");
    /// <summary>
    /// Verification: drops text through the page's own drag listeners, the way a drop from another app arrives. Reports whether
    /// the page took it, whether the outline showed while the drag was over the terminal and in which color, and whether it cleared.
    /// </summary>
    internal async Task<(bool Accepted, bool Outlined, string Outline, bool Cleared)> DropTextAsync(string text)
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.drop(" + JsonSerializer.Serialize(text) + ")"));
        var result = json.RootElement;
        return (result.GetProperty("accepted").GetBoolean(), result.GetProperty("outlined").GetBoolean(),
            result.GetProperty("outline").GetString() ?? "", result.GetProperty("cleared").GetBoolean());
    }
    internal async Task<string> GetSelectionAsync() => JsonSerializer.Deserialize<string>(await ScriptAsync("window.sic.selection()")) ?? "";
    internal async Task ClearAsync() => await ScriptAsync("window.sic.clear()");
    internal async Task<bool> FindAsync(string text, bool backward = false, bool incremental = false) =>
        (await ScriptAsync("window.sic.find(" + JsonSerializer.Serialize(text) + "," + JsonSerializer.Serialize(new { backward, incremental }) + ")")) == "true";
    internal async Task ClearFindAsync() => await ScriptAsync("window.sic.clearFind()");
    internal async Task<string> ReadScreenAsync() => JsonSerializer.Deserialize<string>(await ScriptAsync("window.sic.screenText()")) ?? "";
    /// <summary>The column <paramref name="text"/> starts at on the last line that holds it, -1 when none does, for verification.</summary>
    internal async Task<int> ColumnOfAsync(string text) => JsonSerializer.Deserialize<int>(await ScriptAsync("window.sic.columnOf(" + JsonSerializer.Serialize(text) + ")"));
    /// <summary>The screen row of the last line that holds <paramref name="text"/> (negative in the scrollback, -1000 when none does), for verification.</summary>
    internal async Task<int> RowOfAsync(string text) => JsonSerializer.Deserialize<int>(await ScriptAsync("window.sic.rowOf(" + JsonSerializer.Serialize(text) + ")"));
    /// <summary>What the page's image overlay holds and last drew (window.sic.petImages(), kittyImages.ts), for verification.</summary>
    internal async Task<JsonElement> ReadPetImagesAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.petImages()"));
        return json.RootElement.Clone();
    }
    /// <summary>The renderer's windowsPty option as set, for verification.</summary>
    internal async Task<(string? Backend, int? BuildNumber)> ReadWindowsPtyAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.windowsPty()"));
        var pty = json.RootElement;
        return (pty.TryGetProperty("backend", out var backend) ? backend.GetString() : null,
            pty.TryGetProperty("buildNumber", out var build) ? build.GetInt32() : null);
    }
    /// <summary>Verification: runs a script in the page once it is ready and returns its value as JSON (the hidden-page measurement's probe).</summary>
    internal Task<string> EvaluateForCheckAsync(string script) => ScriptAsync(script);
    internal async Task<(int Columns, int Rows)> GetDimensionsAsync()
    {
        using var json = JsonDocument.Parse(await ScriptAsync("window.sic.dimensions()"));
        return (json.RootElement.GetProperty("columns").GetInt32(), json.RootElement.GetProperty("rows").GetInt32());
    }
    internal async Task FocusInputAsync()
    {
        _webView.Focus(FocusState.Programmatic);
        await ScriptAsync("window.sic.focus()");
    }
    internal async Task CaptureAsync(string path)
    {
        await using var stream = File.Create(path);
        using var randomAccess = stream.AsRandomAccessStream();
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, randomAccess);
    }
    /// <summary>Writes straight to the pseudoconsole, for exit commands that must reach the agent even when the renderer has no focus.</summary>
    internal async Task WriteAsync(string text)
    {
        if (_session is not null && IsRunning) await _session.WriteInputAsync(text);
    }

    /// <summary>True when the process ended within the budget.</summary>
    internal async Task<bool> WaitForExitAsync(TimeSpan budget)
    {
        if (_session is null) return true;
        try { await _session.Completion.WaitAsync(budget); return true; }
        catch (TimeoutException) { return false; }
    }
    internal async Task StopAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
        if (_outputPump is not null) await _outputPump;
    }
    /// <summary>Once, however many callers ask: a tab closing while the app quits disposes its panes from both paths.</summary>
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeOnceAsync());

    private async Task DisposeOnceAsync()
    {
        // Whatever the page wait is doing (before the view joined the window or while its page loads), it ends as a
        // cancellation; a page failure after the close is not reported for a terminal nobody holds any more.
        _loaded.TrySetCanceled();
        _ready.TrySetCanceled();
        await StopAsync();
        _disposed = true;
        _webView.Close();
    }
}
