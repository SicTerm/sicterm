namespace Sic.Core.Appearance;

/// <summary>
/// The Settings panel's tabs, along its bottom edge beside the rail's Settings button
/// (docs/plans/2026-10-05-agent-switches-and-settings-tabs.md). The last one used is saved; an unknown word opens General.
/// Appearance was General's first section until November 12, 2026, when General had grown past a page (docs/decisions/settings-labels.md); its tab is labelled Style, the longest word the bar's six buttons fit at the panel's default width.
/// </summary>
public static class SettingsTabs
{
    public const string General = "general", Appearance = "appearance", Terminal = "terminal", Agents = "agents", About = "about";
    public static IReadOnlyList<string> Choices { get; } = [General, Appearance, Terminal, Agents, About];

    public static string Normalize(string? tab) => tab?.Trim().ToLowerInvariant() is { } word && Choices.Contains(word) ? word : General;

    public static string Label(string? tab) => Normalize(tab) switch { Appearance => "Style", Terminal => "Terminal", Agents => "Agents", About => "About", _ => "General" };
}
