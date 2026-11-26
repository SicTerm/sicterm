using System.Text.RegularExpressions;

namespace Sic.Core.Agents;

/// <summary>
/// The file an agent's tile is read from, and <see cref="Plate"/>, the colour (RRGGBB) of the tile the art is drawn to sit on,
/// for art made for an app's Start tile; null when the art is a whole icon on its own.
/// </summary>
public sealed record AgentIconSource(string Path, string? Plate = null);

/// <summary>
/// An app installed from the Store, as its manifest names its tile: the logo file (<see cref="Logo"/>, the Square44x44Logo
/// asset, absolute), the tile's background colour as the manifest writes it ("#3143FF", or "transparent"), and
/// <see cref="Unplated"/>, the largest of the logo's variants drawn for a taskbar of the dark theme (white art, no tile), or null.
/// </summary>
public sealed record PackagedAppLogo(string Logo, string? BackgroundColor, string? Unplated = null);

/// <summary>
/// Where an agent's tile comes from (docs/decisions/agent-icons.md). SIC ships no agent's logo: a tile is the tool's own icon,
/// read from the user's install of it, or a letter on the agent's hue that SIC draws itself.
/// </summary>
public static partial class AgentIcons
{
    /// <summary>The files an icon is read from: an exe's own icon, an .ico, or a .png.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".exe", ".ico", ".png"];

    /// <summary>The Store package of the Codex desktop app (its Start menu entry reads ChatGPT), whose logo is Codex's tile when it is installed.</summary>
    public const string CodexPackage = "OpenAI.Codex";

    /// <summary>The tile under the Codex app's white knot: black, as ChatGPT's own icon is, which reads on light and dark surfaces alike.</summary>
    public const string CodexPlate = "000000";

    /// <summary>OpenCode's logo, shipped in SIC's Assets\icons (scripts\Build-AgentIcons.ps1), and the light tile it sits on.</summary>
    public const string OpenCodeLogo = "opencode.png", OpenCodePlate = "F1F1F1";

    /// <summary>The letter on an agent's drawn tile. Codex takes X, since Claude Code has the C.</summary>
    public static string Letter(string agentId) => agentId switch
    {
        "hermes" => "H",
        "claude" => "C",
        "codex" => "X",
        "opencode" => "O",
        _ => agentId.Length > 0 ? agentId[..1].ToUpperInvariant() : "?"
    };

    /// <summary>Whether a path can name an agent's icon: absolute, and an exe, an .ico or a .png.</summary>
    public static bool Usable(string? path)
        => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the agent's own icon is read from, or null for SIC's letter tile. The user's choice in Settings comes first while its
    /// file is there. Otherwise the tool's own art, where its install is known to carry it: Claude Code's native installer gives
    /// claude.exe its icon; Hermes keeps its desktop app's icon under its home, beside the bin folder its command is in; Codex's
    /// desktop app, when the Store package <see cref="CodexPackage"/> is installed, has the white OpenAI knot, which sits on a black
    /// tile (<see cref="CodexPlate"/>), or failing that the Start-tile logo on the colour its manifest names. codex.exe itself
    /// carries no icon. opencode.exe carries Bun's, the runtime it is built with, so OpenCode's is the logo SIC ships, in
    /// <paramref name="bundledIcons"/>, SIC's Assets\icons.
    /// <paramref name="detectedPath"/> is where detection found the agent's command, null when it was not found or not yet
    /// probed; <paramref name="packagedApp"/> finds an installed Store package's logo by the package's name.
    /// </summary>
    public static AgentIconSource? Source(AgentDefinition agent, string? detectedPath, Func<string, bool> fileExists, Func<string, PackagedAppLogo?>? packagedApp = null,
        string? bundledIcons = null)
    {
        if (Usable(agent.Icon) && fileExists(agent.Icon!)) return new(agent.Icon!);
        foreach (var candidate in Candidates(agent, detectedPath, packagedApp, bundledIcons))
            if (fileExists(candidate.Path)) return candidate;
        return null;
    }

    /// <summary>A manifest's tile colour as RRGGBB; null for "transparent" or anything that is not a #RRGGBB colour.</summary>
    public static string? PlateColor(string? backgroundColor)
        => backgroundColor is not null && HexColor().IsMatch(backgroundColor) ? backgroundColor.TrimStart('#').ToUpperInvariant() : null;

    /// <summary>
    /// The icon an agent's item in File Explorer's Open in SIC submenu names: the tool's exe when its own icon is in one; otherwise
    /// <paramref name="libraryIcon"/>, the same icon as SIC's tile shows it, written into SIC's icon library (<c>path,-id</c>,
    /// <see cref="Shell.IconLibrary"/>), when there is one; otherwise <paramref name="letterTile"/>, the .ico of SIC's letter tile.
    /// Windows 11's own menu loads icons only from PE resources, so an .ico or a .png the tool keeps beside it cannot be named
    /// there as it is, and both menus keep to one rule.
    /// </summary>
    public static string MenuIcon(AgentIconSource? source, string letterTile, string? libraryIcon = null)
        => source is not null && source.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? source.Path : source is not null && libraryIcon is not null ? libraryIcon : letterTile;

    private static IEnumerable<AgentIconSource> Candidates(AgentDefinition agent, string? detectedPath, Func<string, PackagedAppLogo?>? packagedApp, string? bundledIcons)
    {
        var detected = Usable(detectedPath) ? detectedPath! : null;
        switch (agent.Id)
        {
            case "claude":
                if (detected is not null && detected.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) yield return new(detected);
                break;
            case "hermes":
                // %LOCALAPPDATA%\hermes\bin\hermes.exe beside %LOCALAPPDATA%\hermes\hermes-agent; the home is that folder unless
                // HERMES_HOME moved it, and the smoke points the home at a fixture.
                if (detected is not null && Path.GetDirectoryName(Path.GetDirectoryName(detected)) is { } install) yield return new(HermesIcon(install));
                if (Path.IsPathFullyQualified(agent.HistoryDirectory)) yield return new(HermesIcon(agent.HistoryDirectory));
                break;
            case "codex":
                if (packagedApp?.Invoke(CodexPackage) is { } app)
                {
                    if (Usable(app.Unplated)) yield return new(app.Unplated!, CodexPlate);
                    if (Usable(app.Logo)) yield return new(app.Logo, PlateColor(app.BackgroundColor));
                }
                break;
            case "opencode":
                if (bundledIcons is not null && Path.IsPathFullyQualified(bundledIcons)) yield return new(Path.Combine(bundledIcons, OpenCodeLogo), OpenCodePlate);
                break;
        }
    }

    private static string HermesIcon(string home) => Path.Combine(home, "hermes-agent", "apps", "desktop", "assets", "icon.ico");

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
