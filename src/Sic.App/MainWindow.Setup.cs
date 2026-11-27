using System.Diagnostics;
using Sic.Core.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Sic.App;

/// <summary>
/// The setup notice (<see cref="SetupAdvisor"/>; November 27, 2026): on a PC without PowerShell 7, or where detection found no
/// coding agent, or both, one message with links to the vendors' install pages. At launch it is the window's notice, shown once
/// detection has answered and the first-run welcome has closed, and at every launch until its X is clicked; in Settings → Agents
/// it is a bar at the top of the page until its X is clicked there. Each place remembers the gaps closed in it, the PowerShell
/// one and the agent one apart, and neither shows a gap the PC no longer has: it goes as soon as detection or the shell says so.
/// </summary>
internal sealed partial class MainWindow
{
    private InfoBar? _setupBar;
    /// <summary>The gaps the Settings bar shows now, which its X closes.</summary>
    private SetupGap _setupBarGaps;
    /// <summary>The gaps the launch notice shows while it is the notice up (<see cref="_setupNoticeGeneration"/>), which its X closes.</summary>
    private SetupGap _setupNoticeGaps;
    private int _setupNoticeGeneration = -1;
    /// <summary>The launch notice is waiting for the welcome to close, or for a notice already up to go.</summary>
    private bool _setupNoticeDue;

    /// <summary>The PC's gaps in place of the real ones, for the smoke; null in use.</summary>
    internal SetupGap? SetupGapsOverride { get; set; }

    /// <summary>
    /// What this PC lacks: PowerShell 7 when the shell SicTerm would start is not pwsh.exe (the MSI's or the MSIX's), and an agent
    /// when the last detection found none of the built-in agents; no agent gap while no detection has answered or the last failed.
    /// </summary>
    internal SetupGap SetupGaps
    {
        get
        {
            if (SetupGapsOverride is { } forced) return forced;
            string? shell = null;
            try { shell = AgentDetection.PowerShellPath; }
            catch (FileNotFoundException) { }
            bool? agentFound = _detectionFailed || _detection.Count == 0 ? null : _detection.Values.Any(found => found.Available);
            return SetupAdvisor.Gaps(SetupAdvisor.IsPowerShell7(shell), agentFound);
        }
    }

    /// <summary>Whether the window's notice is the setup notice now; for verification.</summary>
    internal bool SetupNoticeShown => _notice.IsOpen && _noticeGeneration == _setupNoticeGeneration && _setupNoticeGaps != SetupGap.None;

    /// <summary>The notice's own X, and its close by anything else: only the X closes its gaps, and a close lets a waiting notice show.</summary>
    private void WatchSetupNotice()
    {
        _notice.CloseButtonClick += (_, _) => CloseSetupNotice();
        _notice.Closed += (_, _) =>
        {
            if (_setupNoticeDue && _welcomeFilm is null) DispatcherQueue.TryEnqueue(ShowSetupNotice);
        };
    }

    /// <summary>
    /// The launch's notice, once detection has answered: not in the smoke, and not on a launch Open in SicTerm asked for, which
    /// has a dialog or a session to show (the next launch has the notice). It waits for the welcome, and for a notice already up.
    /// </summary>
    private void ShowSetupNoticeAtLaunch()
    {
        if (_options.SmokeTest || _options.Open is not null || _closing) return;
        _setupNoticeDue = true;
        if (_welcomeFilm is null && !_notice.IsOpen) ShowSetupNotice();
    }

    /// <summary>Shows the launch notice for the gaps not closed at launch, when there are any.</summary>
    internal void ShowSetupNotice()
    {
        if (_closing || _welcomeFilm is not null) return;
        // A notice that came up meanwhile is left alone; this one follows when it goes.
        if (_notice.IsOpen && !SetupNoticeShown) return;
        _setupNoticeDue = false;
        var gaps = SetupAdvisor.Open(SetupGaps, _preferences.SetupClosedAtLaunch);
        if (SetupAdvisor.For(gaps) is not { } advice)
        {
            if (SetupNoticeShown) _notice.IsOpen = false;
            _setupNoticeGaps = SetupGap.None;
            return;
        }
        ShowNotice(InfoBarSeverity.Informational, "", advice.Message, null, brief: false, content: SetupLinks(advice));
        _setupNoticeGaps = gaps;
        _setupNoticeGeneration = _noticeGeneration;
    }

    /// <summary>The launch notice's X: its gaps are not shown at launch again.</summary>
    internal void CloseSetupNotice()
    {
        if (_noticeGeneration != _setupNoticeGeneration || _setupNoticeGaps == SetupGap.None) return;
        _preferences.SetupClosedAtLaunch |= _setupNoticeGaps;
        _setupNoticeGaps = SetupGap.None;
        SavePreferences();
    }

    /// <summary>The bar at the top of Settings → Agents.</summary>
    private InfoBar BuildSetupBar()
    {
        var bar = _setupBar = new InfoBar { IsClosable = true, Severity = InfoBarSeverity.Informational, IsOpen = false };
        bar.CloseButtonClick += (_, _) => CloseSetupBar();
        RefreshSetupBar(SetupGaps);
        return bar;
    }

    /// <summary>The Settings bar's X: its gaps are not shown in Settings again.</summary>
    internal void CloseSetupBar()
    {
        if (_setupBarGaps == SetupGap.None) return;
        _preferences.SetupClosedInSettings |= _setupBarGaps;
        _setupBarGaps = SetupGap.None;
        SavePreferences();
        if (_setupBar is not null) _setupBar.IsOpen = false;
    }

    /// <summary>Both places in step with the PC, after each detection: a gap that has gone leaves them, one that has come joins them.</summary>
    internal void RefreshSetupAdvice()
    {
        var gaps = SetupGaps;
        if (SetupNoticeShown && SetupAdvisor.Open(gaps, _preferences.SetupClosedAtLaunch) != _setupNoticeGaps) ShowSetupNotice();
        RefreshSetupBar(gaps);
    }

    private void RefreshSetupBar(SetupGap gaps)
    {
        if (_setupBar is null) return;
        var open = SetupAdvisor.Open(gaps, _preferences.SetupClosedInSettings);
        if (open != _setupBarGaps || _setupBar.IsOpen != (open != SetupGap.None))
        {
            _setupBarGaps = open;
            if (SetupAdvisor.For(open) is { } advice) { _setupBar.Message = advice.Message; _setupBar.Content = SetupLinks(advice); }
            _setupBar.IsOpen = open != SetupGap.None;
        }
    }

    /// <summary>The message's install pages as links in one wrapping line, each opened in the default browser.</summary>
    private static TextBlock SetupLinks(SetupAdvice advice)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        // The dot is held to the link before it by no-break spaces, so a narrow bar wraps after a dot, never before one.
        var separator = new string((char)0xA0, 3) + "·   ";
        foreach (var link in advice.Links)
        {
            if (text.Inlines.Count > 0) text.Inlines.Add(new Run { Text = separator });
            var hyperlink = new Hyperlink();
            // A name stays on one line ("Claude Code" had split across two).
            hyperlink.Inlines.Add(new Run { Text = link.Name.Replace(' ', (char)0xA0) });
            var uri = link.Uri;
            hyperlink.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { Diagnostics.Log("setup link " + uri + ": " + exception.Message); }
            };
            text.Inlines.Add(hyperlink);
        }
        return text;
    }

    /// <summary>The links a setup message carries, by name; for verification.</summary>
    internal static IReadOnlyList<string> SetupLinkNames(object? content) => content is TextBlock text
        ? text.Inlines.OfType<Hyperlink>().Select(link => string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)).Replace((char)0xA0, ' ')).ToList()
        : [];

    internal InfoBar? SetupBar => _setupBar;
}
