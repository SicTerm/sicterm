using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ExplorerMenuTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\SicTerm\current\SicTerm.exe";
    private static readonly MenuItem[] Items =
    [
        new("hermes", "Hermes", @"C:\Users\me\AppData\Local\SicTerm\current\Assets\icons\hermes.ico"),
        new("claude", "Claude Code", @"C:\Users\me\AppData\Local\SicTerm\current\Assets\icons\claude.ico"),
        new("shell", "PowerShell", @"C:\Program Files\PowerShell\7\pwsh.exe")
    ];

    [Fact]
    public void ThreeVerbKeysForFoldersTheirBackgroundsAndDrives()
    {
        Assert.Equal(
        [
            @"Software\Classes\Directory\shell\SIC",
            @"Software\Classes\Directory\Background\shell\SIC",
            @"Software\Classes\Drive\shell\SIC"
        ], ExplorerMenu.Keys);
    }

    [Fact]
    public void TheCommandQuotesTheExeAndTheItemAndNamesTheAgent()
    {
        Assert.Equal("\"" + Exe + "\" --open \"%V\"", ExplorerMenu.Command(Exe));
        Assert.Equal("\"" + Exe + "\" --open \"%V\" --agent codex", ExplorerMenu.Command(Exe, "codex"));
    }

    [Fact]
    public void EachVerbIsASubmenuTitledOpenInSicWithTheExeAsItsIcon()
    {
        var entries = ExplorerMenu.Entries(Exe, Items);
        foreach (var key in ExplorerMenu.Keys)
        {
            Assert.Equal("Open in SicTerm", Value(entries, key, "MUIVerb"));
            // The default value stays unset: set, it displays the entry but empties the cascade.
            Assert.DoesNotContain(entries, entry => entry.Key == key && entry.Name is null);
            Assert.Equal(Exe, Value(entries, key, "Icon"));
            Assert.Equal("", Value(entries, key, "SubCommands"));
            // No Position: Windows sorts the verb among the others.
            Assert.DoesNotContain(entries, entry => entry.Key == key && entry.Name == "Position");
        }
        Assert.Equal(3 * (3 + 4 + 3 * Items.Length), entries.Count);
    }

    [Fact]
    public void NewSessionComesFirstWithASeparatorThenTheItemsInOrder()
    {
        var entries = ExplorerMenu.Entries(Exe, Items);
        var shell = @"Software\Classes\Directory\Background\shell\SIC\shell";
        var itemKeys = entries.Where(entry => entry.Key.StartsWith(shell + "\\", StringComparison.Ordinal) && entry.Name == "MUIVerb")
            .Select(entry => entry.Key[(shell.Length + 1)..]).ToArray();
        Assert.Equal(["00new", "01hermes", "02claude", "03shell"], itemKeys);
        Assert.Equal(itemKeys.OrderBy(key => key, StringComparer.Ordinal), itemKeys);
        // Only the command keys carry a default value.
        Assert.All(entries.Where(entry => entry.Name is null), entry => Assert.EndsWith(@"\command", entry.Key));
        Assert.Equal("New session…", Value(entries, shell + @"\00new", "MUIVerb"));
        Assert.Equal(Exe, Value(entries, shell + @"\00new", "Icon"));
        Assert.Equal(ExplorerMenu.SeparatorAfter, Value(entries, shell + @"\00new", "CommandFlags"));
        Assert.Equal("\"" + Exe + "\" --open \"%V\"", Value(entries, shell + @"\00new\command", null));
        Assert.Equal("Claude Code", Value(entries, shell + @"\02claude", "MUIVerb"));
        Assert.Equal(Items[1].IconPath, Value(entries, shell + @"\02claude", "Icon"));
        Assert.Equal("\"" + Exe + "\" --open \"%V\" --agent claude", Value(entries, shell + @"\02claude\command", null));
        Assert.Equal(Items[2].IconPath, Value(entries, shell + @"\03shell", "Icon"));
    }

    [Fact]
    public void WithoutItemsNewSessionStandsAloneWithoutASeparator()
    {
        var entries = ExplorerMenu.Entries(Exe, []);
        Assert.DoesNotContain(entries, entry => entry.Name == "CommandFlags");
        Assert.Equal(3 * (3 + 3), entries.Count);
    }

    [Fact]
    public void EveryCommandCarriesTheItemPlaceholder()
    {
        var commands = ExplorerMenu.Entries(Exe, Items).Where(entry => entry.Key.EndsWith(@"\command", StringComparison.Ordinal)).Select(entry => (string)entry.Value).ToArray();
        Assert.Equal(3 * (1 + Items.Length), commands.Length);
        Assert.All(commands, command => Assert.Contains("\"%V\"", command));
    }

    [Fact]
    public void ThePackagedCommandReadsTheSameItemsFromOneKeyOutsideClasses()
    {
        var classic = ExplorerMenu.Entries(Exe, Items);
        var package = ExplorerMenu.PackageEntries(Exe, Items);
        Assert.DoesNotContain("Classes", ExplorerMenu.PackageKey);
        Assert.Equal("Open in SicTerm", Value(package, ExplorerMenu.PackageKey, "MUIVerb"));
        Assert.Equal(Exe, Value(package, ExplorerMenu.PackageKey, "Icon"));
        Assert.DoesNotContain(package, entry => entry.Name == "SubCommands");
        // The items are the verb key's items, moved under the package key.
        var verb = ExplorerMenu.Keys[0];
        var classicItems = classic.Where(entry => entry.Key.StartsWith(verb + @"\shell", StringComparison.Ordinal)).Select(entry => entry with { Key = entry.Key[verb.Length..] }).ToArray();
        var packageItems = package.Where(entry => entry.Key.StartsWith(ExplorerMenu.PackageKey + @"\shell", StringComparison.Ordinal)).Select(entry => entry with { Key = entry.Key[ExplorerMenu.PackageKey.Length..] }).ToArray();
        Assert.Equal(classicItems, packageItems);
        Assert.Equal(2 + classicItems.Length, package.Count);
        // A test may give its own key, so a smoke can feed the command without touching the real menu.
        Assert.All(ExplorerMenu.PackageEntries(Exe, Items, @"Software\Test\Menu"), entry => Assert.StartsWith(@"Software\Test\Menu", entry.Key));
    }

    [Fact]
    public void TheMarkIconFollowsTheMenusToneWhenGiven()
    {
        const string light = @"C:\Users\me\AppData\Local\SicTerm\current\Assets\brand\sic-light.ico";
        var entries = ExplorerMenu.Entries(Exe, Items, light);
        foreach (var key in ExplorerMenu.Keys)
        {
            Assert.Equal(light, Value(entries, key, "Icon"));
            Assert.Equal(light, Value(entries, key + @"\shell\00new", "Icon"));
            // The agents keep their own icons, and the commands still run the exe.
            Assert.Equal(Items[0].IconPath, Value(entries, key + @"\shell\01hermes", "Icon"));
            Assert.Equal(ExplorerMenu.Command(Exe), Value(entries, key + @"\shell\00new\command", null));
        }
        Assert.Equal(light, Value(ExplorerMenu.PackageEntries(Exe, Items, markIcon: light), ExplorerMenu.PackageKey, "Icon"));
        Assert.Equal("sic-light.ico", ExplorerMenu.MarkFileFor(lightMenus: true));
        Assert.Equal("sic.ico", ExplorerMenu.MarkFileFor(lightMenus: false));
        // The DLL's resources: the mark in both sets, and one per agent tile.
        Assert.Equal(1, ExplorerMenu.CommandIconIds["sic.ico"]);
        Assert.Equal(2, ExplorerMenu.CommandIconIds["SIC-LIGHT.ICO"]);
        Assert.Equal(4, ExplorerMenu.CommandIconIds.Count(pair => pair.Value >= 101));
    }

    [Fact]
    public void TheExeAMenuRunsIsReadFromNewSessionsCommandNotItsIcon()
    {
        const string light = @"C:\Users\me\AppData\Local\SicTerm\current\Assets\brand\sic-light.ico";
        var classic = ExplorerMenu.Entries(Exe, Items, light);
        var package = ExplorerMenu.PackageEntries(Exe, Items, markIcon: light);
        foreach (var (entries, key) in ExplorerMenu.Keys.Select(key => (classic, key)).Append((package, ExplorerMenu.PackageKey)))
        {
            Assert.Equal(light, Value(entries, key, "Icon"));
            Assert.Equal(Exe, ExplorerMenu.ExecutableOf((string)Value(entries, ExplorerMenu.NewSessionCommandKey(key), null)));
        }
        Assert.Equal(@"C:\Program Files\SIC\SIC.exe", ExplorerMenu.ExecutableOf("\"C:\\Program Files\\SIC\\SIC.exe\" --open \"%V\" --agent claude"));
        Assert.Null(ExplorerMenu.ExecutableOf(@"C:\SIC\SIC.exe --open ""%V"""));
        Assert.Null(ExplorerMenu.ExecutableOf("\"C:\\SIC\\SIC.exe --open"));
        Assert.Null(ExplorerMenu.ExecutableOf("\"\" --open"));
        Assert.Null(ExplorerMenu.ExecutableOf(""));
    }

    [Fact]
    public void TheDriveVerbAloneIsTheThirdKeyAndNothingElse()
    {
        Assert.Equal(@"Software\Classes\Drive\shell\SIC", ExplorerMenu.DriveKey);
        var drive = ExplorerMenu.Entries(Exe, Items, [ExplorerMenu.DriveKey]);
        Assert.All(drive, entry => Assert.StartsWith(ExplorerMenu.DriveKey, entry.Key));
        Assert.Equal(ExplorerMenu.Entries(Exe, Items).Count / 3, drive.Count);
        Assert.Throws<ArgumentException>(() => ExplorerMenu.Entries(Exe, Items, [@"Software\Classes\*\shell\SIC"]));
    }

    [Fact]
    public void ThePackageIdentityNamesTheCertificateSubject()
    {
        Assert.StartsWith("CN=", ExplorerMenu.PackagePublisher);
        Assert.Matches("^[A-Za-z0-9.]+$", ExplorerMenu.PackageName);
        // SIChat, the name before the rename to SicTerm, and SicTerm under the self-signed certificate it was signed with until
        // November 2026 are removed along with the current identity, which is never a former one.
        Assert.Contains(ExplorerMenu.FormerPackageIdentities, identity => identity.Name == "SIChat" && identity.Publisher.StartsWith("CN=", StringComparison.Ordinal));
        Assert.Contains(ExplorerMenu.FormerPackageIdentities, identity => identity.Name == ExplorerMenu.PackageName && identity.Publisher != ExplorerMenu.PackagePublisher);
        Assert.DoesNotContain((ExplorerMenu.PackageName, ExplorerMenu.PackagePublisher), ExplorerMenu.FormerPackageIdentities);
        Assert.EndsWith(".msix", ExplorerMenu.PackageFileName);
        Assert.EndsWith(".dll", ExplorerMenu.CommandFileName);
    }

    [Theory]
    [InlineData("Claude")]
    [InlineData("my agent")]
    [InlineData("")]
    public void AnItemIdThatIsNotAPlainWordIsRefused(string id)
        => Assert.Throws<ArgumentException>(() => ExplorerMenu.Entries(Exe, [new MenuItem(id, "X", Exe)]));

    [Fact]
    public void ARepeatedIdAndARelativeExeAreRefused()
    {
        Assert.Throws<ArgumentException>(() => ExplorerMenu.Entries(Exe, [Items[0], Items[0]]));
        Assert.Throws<ArgumentException>(() => ExplorerMenu.Entries(@"current\SicTerm.exe", Items));
    }

    private static object Value(IEnumerable<RegistryValue> entries, string key, string? name)
        => Assert.Single(entries, entry => entry.Key == key && entry.Name == name).Value;
}
