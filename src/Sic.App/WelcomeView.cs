using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Sic.App;

/// <summary>
/// The first-run film and the card it ends on (docs/decisions/first-run.md): Assets\welcome\welcome.html in a WebView2 laid
/// over the tab area, served from its own virtual host, as the terminal page is. The host sends the theme's colors and, once
/// detection and the first history refresh have answered, what SicTerm found on the PC; the page answers with what the card's
/// buttons asked for. Nothing in the page reaches the network, and no script of the host's runs in it.
/// </summary>
internal sealed class WelcomeView : Grid
{
    private const string Host = "appwelcome.sic.local";
    private const string Page = "https://appwelcome.sic.local/welcome/welcome.html";
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly WebView2 _webView = new() { Opacity = 0 };
    private readonly string _browserDirectory;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<object> _pending = [];
    private Task? _initialization;
    private bool _closed;

    /// <summary>The page has loaded and can take messages.</summary>
    internal Task Ready => _ready.Task;
    /// <summary>Start on the card, or Esc on it: the film is done with.</summary>
    internal event Action? StartRequested;
    /// <summary>Show me around on the card.</summary>
    internal event Action? TourRequested;
    /// <summary>The film's F5 and speaker beats: the host rings the chime, since the page itself plays no sound.</summary>
    internal event Action? ChimeRequested;
    /// <summary>The card has risen, at the film's end or on a skip; for verification.</summary>
    internal event Action? CardShown;

    internal WelcomeView(string browserDirectory, Windows.UI.Color background)
    {
        _browserDirectory = browserDirectory;
        Background = new SolidColorBrush(background);
        _webView.DefaultBackgroundColor = background;
        Children.Add(_webView);
        // Once: the view joins the window a single time, but Loaded can be raised again if it is ever moved.
        Loaded += async (_, _) =>
        {
            if (_closed || _initialization is not null) return;
            _initialization = InitializeAsync();
            try { await _initialization; }
            catch (Exception exception) { _ready.TrySetException(exception); }
        };
    }

    private async Task InitializeAsync()
    {
        var environment = await TerminalView.EnvironmentAsync(_browserDirectory);
        if (_closed) return;
        await _webView.EnsureCoreWebView2Async(environment);
        if (_closed) return;
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
        // Assets, not Assets\welcome, so the page can reach the shipped fonts in Assets\fonts.
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, args) => { if (!string.Equals(args.Uri, Page, StringComparison.OrdinalIgnoreCase)) args.Cancel = true; };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) _ready.TrySetException(new InvalidOperationException("The welcome page failed to load: " + args.WebErrorStatus)); };
        core.WebMessageReceived += HandleMessage;
        core.Navigate(Page);
    }

    private void HandleMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (!string.Equals(args.Source, Page, StringComparison.OrdinalIgnoreCase) || args.WebMessageAsJson.Length > 65_536) return;
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            switch (document.RootElement.GetProperty("type").GetString())
            {
                case "ready":
                    _webView.Opacity = 1;
                    foreach (var message in _pending) Post(message);
                    _pending.Clear();
                    _ready.TrySetResult();
                    _webView.Focus(FocusState.Programmatic);
                    break;
                case "start": StartRequested?.Invoke(); break;
                case "tour": TourRequested?.Invoke(); break;
                case "chime": ChimeRequested?.Invoke(); break;
                case "card": CardShown?.Invoke(); break;
            }
        }
        catch (Exception exception) { Diagnostics.Log("welcome page message: " + exception.Message); }
    }

    /// <summary>Sends the page a message (init, context, skip, replay); one sent before the page is ready goes once it is.</summary>
    internal void Send(object message)
    {
        if (_closed) return;
        if (!_ready.Task.IsCompletedSuccessfully) { _pending.Add(message); return; }
        Post(message);
    }

    private void Post(object message)
    {
        try { _webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, CamelCase)); }
        catch (Exception exception) { Diagnostics.Log("welcome page post: " + exception.Message); }
    }

    /// <summary>The page as it stands, to a PNG; for verification.</summary>
    internal async Task CaptureAsync(string path)
    {
        await using var stream = File.Create(path);
        using var randomAccess = stream.AsRandomAccessStream();
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, randomAccess);
    }

    /// <summary>Ends the page and its browser view; the view is taken out of the window by the caller.</summary>
    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _ready.TrySetCanceled();
        try { _webView.Close(); }
        catch (Exception exception) { Diagnostics.Log("welcome page close: " + exception.Message); }
    }
}
