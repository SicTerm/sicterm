using Sic.Core.Agents;
using Xunit;

namespace Sic.Core.Tests;

public sealed class AgentIconsTests
{
    private const string Home = @"C:\Users\me";
    private const string LocalHermes = @"C:\Users\me\AppData\Local\hermes";
    private const string CodexLogo = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1002.7124.0_x64__2p2nqsd0c76g0\assets\Square44x44Logo.png";
    private static readonly IReadOnlyList<AgentDefinition> Agents = BuiltInAgents.Create(Home, LocalHermes);
    private static AgentDefinition Agent(string id) => Agents.Single(agent => agent.Id == id);
    private static Func<string, bool> Existing(params string[] files) => path => files.Contains(path, StringComparer.OrdinalIgnoreCase);
    private static Func<string, PackagedAppLogo?> Packages(string? backgroundColor = "#3143FF")
        => name => name == AgentIcons.CodexPackage ? new PackagedAppLogo(CodexLogo, backgroundColor) : null;

    [Fact]
    public void ClaudeCodesIconIsItsExe()
    {
        const string exe = @"C:\Users\me\.local\bin\claude.exe";
        Assert.Equal(new AgentIconSource(exe), AgentIcons.Source(Agent("claude"), exe, Existing(exe)));
    }

    [Fact]
    public void ClaudeCodeThroughNpmOrNotYetDetectedHasNone()
    {
        const string shim = @"C:\Users\me\AppData\Roaming\npm\claude.cmd";
        Assert.Null(AgentIcons.Source(Agent("claude"), shim, Existing(shim)));
        Assert.Null(AgentIcons.Source(Agent("claude"), null, _ => true));
    }

    [Fact]
    public void HermesIconIsItsDesktopAppsBesideTheBinFolder()
    {
        const string exe = @"C:\Users\me\AppData\Local\hermes\bin\hermes.exe";
        const string icon = @"C:\Users\me\AppData\Local\hermes\hermes-agent\apps\desktop\assets\icon.ico";
        Assert.Equal(icon, AgentIcons.Source(Agent("hermes"), exe, Existing(icon))?.Path);
        // The home has it too, so it is found before detection has run.
        Assert.Equal(icon, AgentIcons.Source(Agent("hermes"), null, Existing(icon))?.Path);
        Assert.Null(AgentIcons.Source(Agent("hermes"), exe, Existing(exe)));
    }

    [Fact]
    public void HermesWithItsHomeElsewhereIsStillFoundFromItsCommand()
    {
        const string exe = @"D:\Tools\hermes\bin\hermes.exe";
        const string icon = @"D:\Tools\hermes\hermes-agent\apps\desktop\assets\icon.ico";
        var moved = Agent("hermes") with { HistoryDirectory = @"D:\HermesHome" };
        Assert.Equal(icon, AgentIcons.Source(moved, exe, Existing(icon))?.Path);
    }

    [Fact]
    public void CodexsIconIsItsDesktopAppsLogoOnTheAppsTileColour()
    {
        const string exe = @"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe";
        Assert.Equal(new AgentIconSource(CodexLogo, "3143FF"), AgentIcons.Source(Agent("codex"), exe, Existing(CodexLogo), Packages()));
        // Without the app, codex.exe has no icon of its own.
        Assert.Null(AgentIcons.Source(Agent("codex"), exe, _ => true, _ => null));
        Assert.Null(AgentIcons.Source(Agent("codex"), exe, _ => true));
        // A logo the package names but does not hold is no source.
        Assert.Null(AgentIcons.Source(Agent("codex"), exe, Existing(), Packages()));
    }

    [Fact]
    public void ATransparentTileHasNoPlate()
        => Assert.Equal(new AgentIconSource(CodexLogo), AgentIcons.Source(Agent("codex"), null, Existing(CodexLogo), Packages("transparent")));

    [Theory]
    [InlineData("#3143FF", "3143FF")]
    [InlineData("#3143ff", "3143FF")]
    [InlineData("transparent", null)]
    [InlineData("#31F", null)]
    [InlineData(null, null)]
    public void APlateIsTheManifestsHexColour(string? background, string? plate) => Assert.Equal(plate, AgentIcons.PlateColor(background));

    [Fact]
    public void CodexsUnplatedKnotSitsOnBlackBeforeTheAppsTile()
    {
        const string unplated = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1002.7124.0_x64__2p2nqsd0c76g0\assets\Square44x44Logo.targetsize-256_altform-unplated.png";
        Func<string, PackagedAppLogo?> app = name => name == AgentIcons.CodexPackage ? new PackagedAppLogo(CodexLogo, "#3143FF", unplated) : null;
        Assert.Equal(new AgentIconSource(unplated, "000000"), AgentIcons.Source(Agent("codex"), null, Existing(unplated, CodexLogo), app));
        // Gone since it was found: the app's own tile.
        Assert.Equal(new AgentIconSource(CodexLogo, "3143FF"), AgentIcons.Source(Agent("codex"), null, Existing(CodexLogo), app));
    }

    [Fact]
    public void OpenCodesIsTheLogoSicShipsOnItsLightTile()
    {
        const string icons = @"C:\SIC\Assets\icons";
        const string logo = @"C:\SIC\Assets\icons\opencode.png";
        const string command = @"C:\Users\me\AppData\Roaming\npm\opencode.ps1";
        Assert.Equal(new AgentIconSource(logo, "F1F1F1"), AgentIcons.Source(Agent("opencode"), command, Existing(logo), Packages(), icons));
        // opencode.exe carries Bun's icon, not OpenCode's: without SIC's copy there is none.
        Assert.Null(AgentIcons.Source(Agent("opencode"), command, _ => true, Packages()));
    }

    [Fact]
    public void TheUsersChoiceComesFirstWhileItsFileIsThere()
    {
        const string chosen = @"C:\Icons\codex.png";
        var codex = Agent("codex") with { Icon = chosen };
        Assert.Equal(new AgentIconSource(chosen), AgentIcons.Source(codex, null, Existing(chosen, CodexLogo), Packages()));
        Assert.Null(AgentIcons.Source(codex, null, Existing()));
        const string exe = @"C:\Users\me\.local\bin\claude.exe";
        var claude = Agent("claude") with { Icon = @"C:\Icons\gone.ico" };
        Assert.Equal(exe, AgentIcons.Source(claude, exe, Existing(exe))?.Path);
    }

    [Theory]
    [InlineData(@"C:\Icons\tool.exe", true)]
    [InlineData(@"C:\Icons\tool.ICO", true)]
    [InlineData(@"C:\Icons\tool.png", true)]
    [InlineData(@"C:\Icons\tool.svg", false)]
    [InlineData(@"tool.ico", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AnIconIsAnAbsoluteExeIcoOrPng(string? path, bool usable) => Assert.Equal(usable, AgentIcons.Usable(path));

    [Fact]
    public void EachAgentHasADistinctLetter()
    {
        var letters = Agents.Select(agent => AgentIcons.Letter(agent.Id)).ToArray();
        Assert.Equal(["C", "X", "H", "O"], letters);
    }

    [Fact]
    public void TheMenuShowsAnExeTheLibrarysCopyOrTheLetterTile()
    {
        const string tile = @"C:\SIC\Assets\icons\hermes.ico";
        const string library = @"C:\Data\agent-icons\menu-icons-0123456789ab.dll,-103";
        Assert.Equal(@"C:\bin\claude.exe", AgentIcons.MenuIcon(new(@"C:\bin\claude.exe"), tile));
        Assert.Equal(@"C:\bin\claude.exe", AgentIcons.MenuIcon(new(@"C:\bin\claude.exe"), tile, library));
        Assert.Equal(tile, AgentIcons.MenuIcon(new(@"C:\hermes\icon.ico"), tile));
        Assert.Equal(library, AgentIcons.MenuIcon(new(@"C:\hermes\icon.ico"), tile, library));
        Assert.Equal(library, AgentIcons.MenuIcon(new(CodexLogo, "3143FF"), tile, library));
        Assert.Equal(tile, AgentIcons.MenuIcon(new(CodexLogo, "3143FF"), tile));
        // No source, no icon of its own: a library entry left from before does not count.
        Assert.Equal(tile, AgentIcons.MenuIcon(null, tile, library));
    }
}
