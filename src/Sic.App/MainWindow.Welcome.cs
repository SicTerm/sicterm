using Sic.Core.Alerts;
using Sic.Core.Sessions;
using Sic.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sic.App;

/// <summary>
/// The first-run welcome (docs/decisions/first-run.md): the film and its card (<see cref="WelcomeView"/>, Assets\welcome\welcome.html)
/// over the tab area, shown once on a PC and again from Settings → About → Welcome tour; the card's facts come from detection,
/// the catalog and the pins. The tour the card offers is MainWindow.Tour.cs.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>
    /// The welcome's version: a launch whose settings record a smaller number shows it once (<see cref="AppPreferences.WelcomeSeen"/>).
    /// Raised when a release adds a chapter worth a second showing.
    /// </summary>
    internal const int WelcomeVersion = 1;
    private readonly Grid _welcomeHost = new() { Visibility = Visibility.Collapsed };
    private WelcomeView? _welcomeFilm;
    /// <summary>Whether the card has risen in this run, at the film's end or on a skip; for verification.</summary>
    internal bool WelcomeCardShown { get; private set; }
    /// <summary>The film while it shows; for verification.</summary>
    internal WelcomeView? WelcomeFilm => _welcomeFilm;

    /// <summary>
    /// A first run: the settings record no welcome, or one from before <see cref="WelcomeVersion"/>. Not in the smoke or a restore
    /// check, and not on a launch Open in SicTerm asked for, which has a dialog or a session to show; the next launch shows it.
    /// </summary>
    private bool WelcomeDue => _preferences.WelcomeSeen < WelcomeVersion && !_options.SmokeTest && !_options.RestoreOnly && _options.Open is null;

    /// <summary>
    /// Plays the film over the tab area and leaves the card up until Start, Esc or Show me around. Marked seen as it starts, so a
    /// run that ends during the film does not show it again. The theme goes to the page at once; the PC's facts go now when
    /// detection and the first refresh have answered, and again when they do (<see cref="SendWelcomeContext"/>).
    /// </summary>
    internal async Task ShowWelcomeAsync()
    {
        if (_welcomeFilm is not null || _closing) return;
        if (_preferences.WelcomeSeen != WelcomeVersion) { _preferences.WelcomeSeen = WelcomeVersion; SavePreferences(); }
        var view = _welcomeFilm = new WelcomeView(Path.Combine(_options.DataDirectory, "WebView2"), _theme.Base.Color);
        // The card comes first, with Watch the demo, Take the tour and Start using SicTerm; the demo and the tour each come back to it
        // (Chris, November 27, 2026). Start using SicTerm leads on to what this PC still needs (<see cref="StartAfterWelcomeAsync"/>).
        view.StartRequested += () => DispatcherQueue.TryEnqueue(async () => { CloseWelcome(); await StartAfterWelcomeAsync(); });
        view.TourRequested += () => DispatcherQueue.TryEnqueue(async () =>
        {
            // The film stays loaded, out of sight, for the tour to come back to.
            _welcomeHost.Visibility = Visibility.Collapsed;
            _tourFromWelcome = true;
            await StartTourAsync();
        });
        // The film's own beats: the chime the window rings for a finished turn, as a demonstration, whatever Settings → Alerts says.
        view.ChimeRequested += () => { if (!_options.SmokeTest) ChimeSound.Play(SoundCatalog.Find(_preferences.ReadyChime ? _preferences.Sound : null)); };
        view.CardShown += () => WelcomeCardShown = true;
        _welcomeHost.Children.Add(view);
        _welcomeHost.Visibility = Visibility.Visible;
        view.Send(new { type = "init", theme = WelcomeTheme() });
        view.Send(WelcomeContext());
        try { await view.Ready; }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Diagnostics.Log("welcome: " + exception.Message);
            CloseWelcome();
            Notify(InfoBarSeverity.Warning, "The welcome did not open", exception.Message);
        }
    }

    /// <summary>Refreshes that read at least one agent's history: on a first run the first refresh reads none, since the agents are undecided until detection answers.</summary>
    private int _refreshesWithAgents;

    /// <summary>
    /// The facts for the card, sent whenever they may have changed: as the film opens, when detection ends, and after each
    /// refresh. The card says it is still looking until detection has answered and a refresh has read the agents that are on.
    /// </summary>
    private void SendWelcomeContext() => _welcomeFilm?.Send(WelcomeContext());

    private bool WelcomeFactsSettled => !_detecting && (_detectionFailed || _detection.Count > 0) && (_refreshesWithAgents > 0 || _preferences.Agents.All(agent => !agent.IsOn));

    /// <summary>The tour was started from the welcome's card, and goes back to it when it ends.</summary>
    private bool _tourFromWelcome;

    /// <summary>The card again, after a tour started from it.</summary>
    private void ReturnToWelcomeCard()
    {
        if (!_tourFromWelcome) return;
        _tourFromWelcome = false;
        if (_welcomeFilm is not { } view || _closing) return;
        _welcomeHost.Visibility = Visibility.Visible;
        view.Send(new { type = "card" });
        view.FocusPage();
    }

    /// <summary>
    /// Start using SicTerm: the home screen shows Get set up while an agent or PowerShell 7 is missing; with an agent and no folder
    /// listed, Add folders opens, since the person asked to start.
    /// </summary>
    private async Task StartAfterWelcomeAsync()
    {
        if (_closing || Folders.Count > 0 || !_detection.Values.Any(found => found.Available)) return;
        await ShowFolderWizardAsync();
    }

    /// <summary>Takes the film away and gives the keyboard back to the window.</summary>
    private void CloseWelcome()
    {
        if (_welcomeFilm is not { } view) return;
        _welcomeFilm = null;
        _welcomeHost.Children.Remove(view);
        _welcomeHost.Visibility = Visibility.Collapsed;
        view.Close();
        if (_activePane is { } pane) pane.FocusInput(); else _root.Focus(FocusState.Programmatic);
        // The setup notice waited for the welcome (MainWindow.Setup.cs).
        if (_setupNoticeDue) ShowSetupNotice();
    }

    /// <summary>
    /// The chrome's colors and display face, for the page's variables; the miniature's terminals keep the fixed palette as the
    /// panes do, and its tab strip takes the strip ink, as the real one does, since a light theme's rail may be dark or light.
    /// </summary>
    private object WelcomeTheme()
    {
        static string Rgba(Windows.UI.Color color) => $"rgba({color.R},{color.G},{color.B},{(color.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
        var definition = _theme.Definition;
        return new
        {
            @base = Theme.Hex(_theme.Base.Color), rail = Theme.Hex(Theme.Over(_theme.Rail.Color, _theme.Base.Color)), panel = Theme.Hex(Theme.Over(_theme.Panel.Color, _theme.Base.Color)),
            layer = Rgba(_theme.Layer.Color), hover = Rgba(_theme.Hover.Color), stroke = Rgba(_theme.Stroke.Color), strokeStrong = Rgba(_theme.StrokeStrong.Color),
            text = Rgba(_theme.Text.Color), textSecondary = Rgba(_theme.TextSecondary.Color), textTertiary = Rgba(_theme.TextTertiary.Color), stripText = Rgba(_theme.StripText.Color),
            accent = Theme.Hex(_theme.Accent.Color), accentText = Theme.Hex(_theme.AccentText.Color), accentSoft = Rgba(_theme.AccentSoft.Color),
            success = Theme.Hex(_theme.SuccessDot.Color), warning = Theme.Hex(_theme.WarningDot.Color), danger = Theme.Hex(_theme.Danger.Color),
            hermes = "#" + definition.AgentHue("hermes"), claude = "#" + definition.AgentHue("claude"), codex = "#" + definition.AgentHue("codex"), opencode = "#" + definition.AgentHue("opencode"),
            displayFont = definition.DisplayFont, name = definition.Name
        };
    }

    /// <summary>
    /// What the card says about this PC: the agents detection found among those that are on, how many conversations and folders
    /// the catalog holds, the latest conversation, and the folders pinned. The film lays only the agents over its script; its
    /// folders are its own examples, never the PC's (a personal folder name had shown up in it; October 18, 2026).
    /// </summary>
    private object WelcomeContext()
    {
        var agents = _preferences.Agents.Where(agent => agent.IsOn && _detection.GetValueOrDefault(agent.Id)?.Available == true)
            .Select(agent => new { id = agent.Id, name = agent.Name }).ToArray();
        var hidden = _preferences.Agents.Where(agent => !agent.IsOn).Select(agent => agent.Id).ToArray();
        IReadOnlyList<Conversation> recent = [];
        var conversations = 0;
        var folders = 0;
        try
        {
            recent = _catalog.Search("", limit: 200, aliases: _preferences.ConversationNames, hiddenAgents: hidden);
            conversations = _catalog.Count(hidden);
            folders = _catalog.FolderActivity(DateTimeOffset.MinValue).Select(item => PinnedFolders.Normalize(item.Folder ?? "")).Where(key => key.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
        catch (Exception exception) { Diagnostics.Log("welcome context: " + exception.Message); }
        var latest = recent.FirstOrDefault();
        return new
        {
            type = "context",
            ready = WelcomeFactsSettled,
            agents,
            conversations,
            folders,
            pinned = Folders.Count,
            latest = latest is null ? null : new { agent = latest.AgentId, title = latest.Title, folder = FolderOf(latest) is { Length: > 0 } folder ? Paths.Leaf(folder) : "a folder SicTerm does not know yet", when = Ago(latest.UpdatedAt) }
        };
    }

    /// <summary>How long ago, in words: "just now", "20 minutes ago", "3 hours ago", "yesterday", "4 days ago", "on October 2".</summary>
    internal static string Ago(DateTimeOffset time, DateTimeOffset? now = null)
    {
        var elapsed = (now ?? DateTimeOffset.Now) - time;
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) { var minutes = (int)elapsed.TotalMinutes; return minutes == 1 ? "a minute ago" : minutes + " minutes ago"; }
        if (elapsed < TimeSpan.FromHours(24)) { var hours = (int)elapsed.TotalHours; return hours == 1 ? "an hour ago" : hours + " hours ago"; }
        if (elapsed < TimeSpan.FromDays(2)) return "yesterday";
        if (elapsed < TimeSpan.FromDays(14)) return (int)elapsed.TotalDays + " days ago";
        return "on " + time.ToLocalTime().ToString("MMMM d", System.Globalization.CultureInfo.InvariantCulture);
    }
}
