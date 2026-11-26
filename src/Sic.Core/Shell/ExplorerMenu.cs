using System.Text.RegularExpressions;

namespace Sic.Core.Shell;

/// <summary>
/// One registry value the Explorer menu needs: under <see cref="Key"/>, a path relative to the current user's hive, the value
/// named <see cref="Name"/> (null for the key's default value) set to <see cref="Value"/>: a string, or an int for a DWORD.
/// </summary>
public sealed record RegistryValue(string Key, string? Name, object Value);

/// <summary>An item of the Open in SIC submenu: an agent that is on, or PowerShell. <see cref="IconPath"/> is an .ico or an exe.</summary>
public sealed record MenuItem(string Id, string Name, string IconPath);

/// <summary>
/// The static verbs that put <b>Open in SicTerm</b> on File Explorer's right-click menu for a folder, the space inside a folder, and
/// a drive, as a submenu: <b>New session…</b>, which opens the New session dialog with that folder, then one item per agent,
/// each starting a session there at once. Windows reads the verbs from the registry, under the current user, so nothing needs
/// signing or admin rights; on Windows 11 they sit under Show more options (docs/decisions/explorer-menu.md). This class only
/// lays the values out; Sic.App writes them, since Core has no registry.
/// </summary>
public static partial class ExplorerMenu
{
    public const string Verb = "SIC";
    public const string Title = "Open in SicTerm";
    public const string NewSessionTitle = "New session…";
    /// <summary>The key of the New session item under each verb's <c>shell</c>: a leading digit keeps it ahead of the agents, since Windows lists subcommands by key name.</summary>
    public const string NewSessionKey = "00new";
    /// <summary>The <c>CommandFlags</c> bit that draws a separator after an item (ECF_SEPARATORAFTER).</summary>
    public const int SeparatorAfter = 0x40;

    /// <summary>Where a verb goes for a right-click on a folder, inside a folder, and on a drive.</summary>
    public static readonly IReadOnlyList<string> Roots =
        [@"Software\Classes\Directory\shell", @"Software\Classes\Directory\Background\shell", @"Software\Classes\Drive\shell"];

    /// <summary>The three verb keys, each of which <see cref="Entries(string, IEnumerable{MenuItem})"/> fills and an unregister deletes whole.</summary>
    public static IReadOnlyList<string> Keys { get; } = Roots.Select(root => root + "\\" + Verb).ToArray();

    /// <summary>
    /// The drive's verb key, which stays a classic verb even with the identity package registered: the package's context-menu
    /// extension takes only files, folders and folder backgrounds, so a drive's Open in SIC is under Show more options.
    /// </summary>
    public static string DriveKey => Keys[2];

    /// <summary>
    /// Where the packaged command (SicExplorerCommand.dll, Windows 11's own menu) reads the menu from: the same shape as a verb
    /// key, title and icon at the root and the items under <c>shell</c>, but outside <c>Classes</c>, so Windows reads no verb
    /// from it. <see cref="PackageEntries"/> fills it; the package's command enumerates it on every right-click.
    /// </summary>
    public const string PackageKey = @"Software\Super-Intelligent-Chat\ExplorerMenu";

    /// <summary>The identity package's name, its publisher (the signing certificate's subject, exactly) and its application id, as its manifest states them.</summary>
    public const string PackageName = "SicTerm";
    public const string PackagePublisher = "CN=Christopher Luton, O=Christopher Luton, L=Austin, S=tx, C=US";
    public const string PackageApplicationId = "SIC";

    /// <summary>
    /// The identities the package had before, as name and publisher, removed along with the current one so that a renamed package
    /// never stays registered beside its successor, both putting Open in SIC on the menu: SIChat until the rename to SicTerm on
    /// October 13, 2026, and SicTerm under the self-signed certificate until Azure Artifact Signing took over in November 2026.
    /// A new signing certificate changes the publisher, and the identity it leaves behind belongs here too.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Publisher)> FormerPackageIdentities =
    [
        ("SIChat", "CN=Super-Intelligent-Chat-Selfie, O=Chris Luton"),
        ("SicTerm", "CN=Super-Intelligent-Chat-Selfie, O=Chris Luton")
    ];

    /// <summary>The identity package and the command DLL, beside SicTerm.exe when the build had the certificate and the C++ tools.</summary>
    public const string PackageFileName = "SicTerm.Identity.msix";
    public const string CommandFileName = "SicExplorerCommand.dll";

    /// <summary>
    /// The icon resources built into the command DLL (SicExplorerCommand.rc), by the .ico they come from. Windows 11's own menu
    /// loads an icon only from a PE resource, so the DLL answers "its path,-id" for these; SIC's mark is 1 (the white set, for
    /// dark menus) and 2 (the near-black set, for light menus), chosen by the DLL from Windows' app mode when the menu opens.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> CommandIconIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["sic.ico"] = 1, ["sic-light.ico"] = 2, ["claude.ico"] = 101, ["codex.ico"] = 102, ["hermes.ico"] = 103, ["opencode.ico"] = 104
    };

    /// <summary>The .ico of SIC's mark for a menu of this tone: the near-black set on light menus, the white set on dark ones.</summary>
    public static string MarkFileFor(bool lightMenus) => lightMenus ? "sic-light.ico" : "sic.ico";

    /// <summary>
    /// The command a verb runs. <c>%V</c> is what Explorer replaces with the item's path, and for the space inside a folder, with
    /// that folder's; <c>%1</c> is empty there, so <c>%V</c> serves all three keys.
    /// </summary>
    public static string Command(string executablePath, string? agentId = null)
        => "\"" + executablePath + "\" --open \"%V\"" + (agentId is null ? "" : " --agent " + agentId);

    /// <summary>
    /// Where New session's command is under a verb key or <see cref="PackageKey"/>: the value that says which copy of SIC the
    /// menu runs. The Icon values do not say it, being SIC's mark beside the exe rather than the exe.
    /// </summary>
    public static string NewSessionCommandKey(string key) => key + @"\shell\" + NewSessionKey + @"\command";

    /// <summary>The exe a <see cref="Command"/> runs, the quoted path it starts with; null for a command not laid out that way.</summary>
    public static string? ExecutableOf(string command)
    {
        if (!command.StartsWith('"')) return null;
        var end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }

    /// <summary>
    /// Every value for the verbs that point at <paramref name="executablePath"/>, with <paramref name="items"/> as the submenu
    /// after New session, in the order given; the item keys carry an index so the registry's alphabetical order keeps it.
    /// </summary>
    public static IReadOnlyList<RegistryValue> Entries(string executablePath, IEnumerable<MenuItem> items, string? markIcon = null) => Entries(executablePath, items, Keys, markIcon);

    /// <summary>
    /// The same verbs for a chosen set of the keys in <see cref="Keys"/>: the drive's alone beside the identity package.
    /// <paramref name="markIcon"/> is the .ico for Open in SIC and New session… (<see cref="MarkFileFor"/>, in the set for the
    /// menu's tone); the exe's own icon, the white set, when null.
    /// </summary>
    public static IReadOnlyList<RegistryValue> Entries(string executablePath, IEnumerable<MenuItem> items, IEnumerable<string> verbKeys, string? markIcon = null)
    {
        var list = Checked(executablePath, items);
        var values = new List<RegistryValue>();
        foreach (var key in verbKeys)
        {
            if (!Keys.Contains(key)) throw new ArgumentException("Not one of the Open in SicTerm verb keys: " + key, nameof(verbKeys));
            // The title goes in MUIVerb and the key's default value stays unset: with the title in the default value Windows showed
            // the entry but an empty submenu (October 7, 2026), as Microsoft's "Creating Static Cascading Menus" warns.
            values.Add(new(key, "MUIVerb", Title));
            values.Add(new(key, "Icon", markIcon ?? executablePath));
            // Empty: the subcommands are the verbs under this key's own shell subkey.
            values.Add(new(key, "SubCommands", ""));
            AddItems(values, key, executablePath, markIcon ?? executablePath, list);
        }
        return values;
    }

    /// <summary>
    /// The menu as the packaged command reads it, under <paramref name="key"/> (<see cref="PackageKey"/> unless a test gives its
    /// own): title and icon, then the same items as <see cref="Entries(string, IEnumerable{MenuItem}, string?)"/> under <c>shell</c>,
    /// without the verb's SubCommands.
    /// </summary>
    public static IReadOnlyList<RegistryValue> PackageEntries(string executablePath, IEnumerable<MenuItem> items, string key = PackageKey, string? markIcon = null)
    {
        var list = Checked(executablePath, items);
        var values = new List<RegistryValue> { new(key, "MUIVerb", Title), new(key, "Icon", markIcon ?? executablePath) };
        AddItems(values, key, executablePath, markIcon ?? executablePath, list);
        return values;
    }

    private static List<MenuItem> Checked(string executablePath, IEnumerable<MenuItem> items)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("The executable path must be absolute: " + executablePath, nameof(executablePath));
        var list = items.ToList();
        foreach (var item in list)
        {
            if (!AgentId().IsMatch(item.Id)) throw new ArgumentException("A menu item id is a word of lower-case letters and digits: " + item.Id, nameof(items));
            if (list.Count(other => other.Id == item.Id) > 1) throw new ArgumentException("A menu item id appears twice: " + item.Id, nameof(items));
        }
        return list;
    }

    private static void AddItems(List<RegistryValue> values, string key, string executablePath, string markIcon, List<MenuItem> list)
    {
        var shell = key + @"\shell";
        var open = shell + "\\" + NewSessionKey;
        values.Add(new(open, "MUIVerb", NewSessionTitle));
        values.Add(new(open, "Icon", markIcon));
        if (list.Count > 0) values.Add(new(open, "CommandFlags", SeparatorAfter));
        values.Add(new(NewSessionCommandKey(key), null, Command(executablePath)));
        for (var index = 0; index < list.Count; index++)
        {
            var item = shell + "\\" + ItemKey(index, list[index].Id);
            values.Add(new(item, "MUIVerb", list[index].Name));
            values.Add(new(item, "Icon", list[index].IconPath));
            values.Add(new(item + @"\command", null, Command(executablePath, list[index].Id)));
        }
    }

    /// <summary>An item's key under a verb's <c>shell</c>: its place in the menu, two digits, then its id.</summary>
    public static string ItemKey(int index, string id) => (index + 1).ToString("00") + id;

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex AgentId();
}
