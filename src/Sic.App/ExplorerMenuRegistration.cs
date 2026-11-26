using Microsoft.Win32;
using Sic.Core.Agents;
using Sic.Core.Shell;
using Sic.Core.State;
using Windows.Management.Deployment;

namespace Sic.App;

/// <summary>How Open in SIC is on File Explorer's menu for this user, if at all (docs/decisions/explorer-menu.md).</summary>
internal enum ExplorerMenuMode
{
    None,
    /// <summary>Static verbs under HKCU\Software\Classes: Windows 11 shows them under Show more options.</summary>
    Classic,
    /// <summary>The identity package with SicExplorerCommand.dll: Windows 11's own menu, and the classic one too.</summary>
    Package
}

/// <summary>
/// Puts Open in SIC on File Explorer's menu and takes it off, for the current user. Two ways, one copy of SIC owning the entry at a
/// time, whichever registered last: the identity package when it was built beside the exe and the PC trusts its certificate
/// (<see cref="ExplorerMenuMode.Package"/>), the static verbs otherwise (<see cref="ExplorerMenuMode.Classic"/>). Setup and the
/// updater register the installed copy through Velopack's hooks (<see cref="TryRegisterInstalled"/>), a copy run from a build
/// folder through the switch in Settings → General. Nothing here catches a failure except the hooks, which must never fail an
/// install, and the package attempt, which falls back to the verbs and reports why.
/// </summary>
internal static class ExplorerMenuRegistration
{
    /// <summary>The outcome of a registration: how the entry is in the menu, and why the package was not used when it was not.</summary>
    internal sealed record Result(ExplorerMenuMode Mode, string? PackageProblem);

    /// <summary>
    /// Whether the build beside <paramref name="executablePath"/> carries the identity package and the command DLL, on a PC whose
    /// File Explorer can load it: the DLL is x64, and an ARM64 Explorer cannot load it, so an ARM64 PC keeps the classic verbs.
    /// </summary>
    internal static bool PackageAvailable(string executablePath)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64) return false;
        var directory = Path.GetDirectoryName(executablePath)!;
        return File.Exists(Path.Combine(directory, ExplorerMenu.PackageFileName)) && File.Exists(Path.Combine(directory, ExplorerMenu.CommandFileName));
    }

    /// <summary>
    /// Registers the copy at <paramref name="executablePath"/>: the package when it is there and Windows takes it, the verbs
    /// otherwise. Whatever the other way had left is removed, so one copy, one way, owns the entry.
    /// </summary>
    internal static async Task<Result> RegisterAsync(string executablePath, IEnumerable<AgentDefinition> agents, Func<string, AgentIconSource?>? iconSource = null, Func<string, string?>? libraryIcon = null)
    {
        var items = Items(executablePath, agents, iconSource, libraryIcon);
        string? problem = null;
        if (PackageAvailable(executablePath))
        {
            try
            {
                DeleteClassic();
                // The package's menu for folders and their backgrounds; a drive keeps the classic verb, which the package cannot cover.
                Write(PackageModeEntries(executablePath, items), PackageModeRoots);
                await RegisterPackageAsync(executablePath);
                return new Result(ExplorerMenuMode.Package, null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                problem = exception.Message;
                Registry.CurrentUser.DeleteSubKeyTree(ExplorerMenu.PackageKey, throwOnMissingSubKey: false);
            }
        }
        await RemovePackageAsync();
        Write(ClassicEntries(executablePath, items), ExplorerMenu.Keys.ToArray());
        return new Result(ExplorerMenuMode.Classic, problem);
    }

    private static readonly string[] PackageModeRoots = [ExplorerMenu.PackageKey, ExplorerMenu.DriveKey];

    private static IReadOnlyList<RegistryValue> PackageModeEntries(string executablePath, List<MenuItem> items)
        => ExplorerMenu.PackageEntries(executablePath, items, markIcon: MarkIcon(executablePath)).Concat(ExplorerMenu.Entries(executablePath, items, [ExplorerMenu.DriveKey], MarkIcon(executablePath))).ToArray();

    private static IReadOnlyList<RegistryValue> ClassicEntries(string executablePath, List<MenuItem> items)
        => ExplorerMenu.Entries(executablePath, items, MarkIcon(executablePath));

    /// <summary>
    /// SIC's mark for the menus as they are now: the near-black set while Windows' app mode is light, the white set while it is
    /// dark, from the .ico files beside the exe. Written into the entries, so the classic menu shows the right set; the packaged
    /// command decides for itself when the menu opens, and <see cref="MainWindow.SyncExplorerMenu"/> rewrites the entries when the
    /// mode changes while SIC runs.
    /// </summary>
    internal static string MarkIcon(string executablePath)
        => Path.Combine(Path.GetDirectoryName(executablePath)!, "Assets", "brand", ExplorerMenu.MarkFileFor(ShellIcons.LightMenus()));

    /// <summary>Takes the entry off, whichever way and whichever copy put it there; nothing to remove is not an error.</summary>
    internal static async Task UnregisterAsync()
    {
        DeleteClassic();
        Registry.CurrentUser.DeleteSubKeyTree(ExplorerMenu.PackageKey, throwOnMissingSubKey: false);
        await RemovePackageAsync();
    }

    /// <summary>
    /// The exe the entry points at, from its New session command, or null when there is no entry. Not from its Icon value, which
    /// has been SIC's mark beside the exe since October 7, 2026: read from there, no copy ever owned the menu, so the switch in
    /// Settings showed off, the submenu was not kept in step, and an uninstall left the entry behind (found October 13, 2026).
    /// </summary>
    internal static string? RegisteredExecutable() => Mode() switch
    {
        ExplorerMenuMode.Package => ReadExecutable(ExplorerMenu.PackageKey),
        ExplorerMenuMode.Classic => ReadExecutable(ExplorerMenu.Keys[0]),
        _ => null
    };

    /// <summary>The exe that New session runs in the menu under <paramref name="keyPath"/>, or null when there is no such command.</summary>
    internal static string? ReadExecutable(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ExplorerMenu.NewSessionCommandKey(keyPath));
        return key?.GetValue("") is string command ? ExplorerMenu.ExecutableOf(command) : null;
    }

    /// <summary>How the entry is registered now, from the registry alone (the package's own registration is not consulted).</summary>
    internal static ExplorerMenuMode Mode()
    {
        if (ReadIcon(ExplorerMenu.PackageKey) is not null) return ExplorerMenuMode.Package;
        return ReadIcon(ExplorerMenu.Keys[0]) is not null ? ExplorerMenuMode.Classic : ExplorerMenuMode.None;
    }

    /// <summary>The entries the current mode would hold for this copy and these agents, to compare with <see cref="IsCurrent"/>.</summary>
    internal static IReadOnlyList<RegistryValue> ExpectedEntries(string executablePath, IEnumerable<AgentDefinition> agents, Func<string, AgentIconSource?>? iconSource = null, Func<string, string?>? libraryIcon = null)
        => Mode() == ExplorerMenuMode.Package ? PackageModeEntries(executablePath, Items(executablePath, agents, iconSource, libraryIcon)) : ClassicEntries(executablePath, Items(executablePath, agents, iconSource, libraryIcon));

    /// <summary>Rewrites the current mode's entries in place; the package, if that is the mode, stays as it is.</summary>
    internal static void Refresh(IReadOnlyList<RegistryValue> entries)
        => Write(entries, Mode() == ExplorerMenuMode.Package ? PackageModeRoots : ExplorerMenu.Keys.ToArray());

    /// <summary>Whether the registry holds exactly these values: the same values, and no item key the entries lack.</summary>
    internal static bool IsCurrent(IReadOnlyList<RegistryValue> entries)
    {
        foreach (var entry in entries)
        {
            using var key = Registry.CurrentUser.OpenSubKey(entry.Key);
            if (key?.GetValue(entry.Name ?? "") is not { } value || !Equals(value, entry.Value)) return false;
        }
        var expected = entries.Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in entries.Select(entry => entry.Key).Where(key => !key.Contains(@"\shell\", StringComparison.OrdinalIgnoreCase)).Distinct())
        {
            using var shell = Registry.CurrentUser.OpenSubKey(root + @"\shell");
            if (shell is null) return false;
            if (shell.GetSubKeyNames().Any(name => !expected.Contains(root + @"\shell\" + name))) return false;
        }
        return true;
    }

    /// <summary>Writes every value under <paramref name="roots"/>, having first removed those keys, so an item that went away goes away from the menu too.</summary>
    internal static void Write(IReadOnlyList<RegistryValue> entries, params string[] roots)
    {
        foreach (var root in roots) Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        foreach (var entry in entries)
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.Key, writable: true) ?? throw new InvalidOperationException("The registry key could not be created: " + entry.Key);
            key.SetValue(entry.Name ?? "", entry.Value);
        }
    }

    private static void DeleteClassic()
    {
        foreach (var key in ExplorerMenu.Keys) Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
    }

    private static string? ReadIcon(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue("Icon") as string;
    }

    /// <summary>
    /// The submenu for a copy of SIC at <paramref name="executablePath"/>: the agents that are not off (an agent still undecided
    /// counts, since detection has yet to settle it), in Settings' order, each with the tool's own exe where its icon is in one, the
    /// tile's icon from SIC's icon library where it is in there, and SIC's letter tile beside the exe otherwise
    /// (<see cref="AgentIcons.MenuIcon"/>; <paramref name="iconSource"/> gives the source of an agent's tile, none when null, and
    /// <paramref name="libraryIcon"/> its entry in the library), then PowerShell with pwsh.exe's own icon, or SIC's when PowerShell
    /// 7 is not found.
    /// </summary>
    internal static List<MenuItem> Items(string executablePath, IEnumerable<AgentDefinition> agents, Func<string, AgentIconSource?>? iconSource = null, Func<string, string?>? libraryIcon = null)
    {
        var icons = Path.Combine(Path.GetDirectoryName(executablePath)!, "Assets", "icons");
        var items = agents.Where(agent => agent.Enabled != false)
            .Select(agent => new MenuItem(agent.Id, agent.Name, AgentIcons.MenuIcon(iconSource?.Invoke(agent.Id), Path.Combine(icons, agent.Id + ".ico"), libraryIcon?.Invoke(agent.Id)))).ToList();
        string shellIcon;
        try { shellIcon = AgentDetection.PowerShellPath; }
        catch (FileNotFoundException) { shellIcon = executablePath; }
        items.Add(new MenuItem("shell", "PowerShell", shellIcon));
        return items;
    }

    /// <summary>
    /// Registers the identity package beside the exe with the exe's folder as its external location, having removed the one
    /// registered before (the same version cannot be added twice, and another copy's package must not stay). Throws with Windows'
    /// reason when the package is refused: a certificate the PC does not trust gives 0x800B0109.
    /// </summary>
    private static async Task RegisterPackageAsync(string executablePath)
    {
        await RemovePackageAsync();
        var directory = Path.GetDirectoryName(executablePath)!;
        var manager = new PackageManager();
        var options = new AddPackageOptions { ExternalLocationUri = new Uri(directory + Path.DirectorySeparatorChar) };
        var result = await manager.AddPackageByUriAsync(new Uri(Path.Combine(directory, ExplorerMenu.PackageFileName)), options);
        if (result.ExtendedErrorCode is { } error && error.HResult != 0)
            throw new InvalidOperationException("Windows did not register SicTerm's identity package: " + (string.IsNullOrWhiteSpace(result.ErrorText) ? error.Message : result.ErrorText) + " (0x" + error.HResult.ToString("X8") + ").");
    }

    /// <summary>
    /// Removes every registration of SIC's identity package for this user, under its current identity or a former one
    /// (<see cref="ExplorerMenu.FormerPackageIdentities"/>, such as SIChat's); none is not an error.
    /// </summary>
    private static async Task RemovePackageAsync()
    {
        var manager = new PackageManager();
        var identities = ExplorerMenu.FormerPackageIdentities.Append((Name: ExplorerMenu.PackageName, Publisher: ExplorerMenu.PackagePublisher)).ToList();
        var packages = manager.FindPackagesForUserWithPackageTypes("", PackageTypes.Main)
            .Where(package => identities.Any(identity => string.Equals(package.Id.Name, identity.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(package.Id.Publisher, identity.Publisher, StringComparison.Ordinal)))
            .ToList();
        foreach (var package in packages)
        {
            var result = await manager.RemovePackageAsync(package.Id.FullName);
            if (result.ExtendedErrorCode is { } error && error.HResult != 0)
                throw new InvalidOperationException("Windows did not remove SicTerm's identity package: " + (string.IsNullOrWhiteSpace(result.ErrorText) ? error.Message : result.ErrorText) + " (0x" + error.HResult.ToString("X8") + ").");
        }
    }

    /// <summary>The agents the default data folder's settings name, with every built-in agent they lack; the built-in agents when there are no settings.</summary>
    private static IEnumerable<AgentDefinition> AgentsOfThisUser()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StartupOptions.DataFolderName);
        IEnumerable<AgentDefinition> agents;
        try { agents = new JsonStateStore<AppPreferences>(Path.Combine(data, "settings.json")).Load(() => new AppPreferences()).Agents ?? []; }
        catch (InvalidDataException) { agents = []; }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return BuiltInAgents.Merge(agents, BuiltInAgents.Create(home, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes")));
    }

    /// <summary>
    /// The tiles' sources outside a running SIC (Setup and the updater): what the default data folder's icon cache last read, or
    /// what can be found without detection, as Hermes's icon and OpenCode's can.
    /// </summary>
    private static Func<string, AgentIconSource?> InstalledIconSource(IReadOnlyList<AgentDefinition> agents, AgentIconCache cache)
        => id => cache.CachedSource(id) ?? (agents.FirstOrDefault(agent => agent.Id == id) is { } agent ? AgentIcons.Source(agent, null, File.Exists, bundledIcons: MainWindow.BundledIcons) : null);

    private static AgentIconCache InstalledIconCache()
        => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StartupOptions.DataFolderName, "agent-icons"));

    /// <summary>
    /// Velopack's after-install and after-update hooks. A fresh install registers; an update registers again only while the entry
    /// is SIC's, so one turned off in Settings stays off. Never throws: the menu is a convenience, Setup is not.
    /// </summary>
    internal static void TryRegisterInstalled(bool fresh)
    {
        try
        {
            if (!fresh && Mode() == ExplorerMenuMode.None) return;
            if (Environment.ProcessPath is not { } executable) return;
            var agents = AgentsOfThisUser().ToList();
            var cache = InstalledIconCache();
            RegisterAsync(executable, agents, InstalledIconSource(agents, cache), cache.MenuIcon).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* Setup goes on; the switch in Settings can register later. */ }
    }

    /// <summary>Velopack's before-uninstall hook: removes the entry when it points at this copy, and leaves another copy's alone.</summary>
    internal static void TryUnregisterInstalled()
    {
        try
        {
            if (RegisteredExecutable() is { } registered && string.Equals(registered, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) UnregisterAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* The uninstall goes on. */ }
    }
}
