using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PersistentEnvironmentTests
{
    private static Dictionary<string, string> Values(params (string Name, string Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Combine_PutsTheUsersVariablesOverThePcsAndTheUsersPathAfterThePcs()
    {
        var combined = PersistentEnvironment.Combine(
            Values(("Path", @"C:\Windows\system32;C:\Program Files\nodejs;"), ("TEMP", @"C:\Windows\Temp"), ("OS", "Windows_NT")),
            Values(("PATH", @"C:\Users\someone\.local\bin"), ("TEMP", @"C:\Users\someone\AppData\Local\Temp")));
        Assert.Equal(@"C:\Windows\system32;C:\Program Files\nodejs;C:\Users\someone\.local\bin", combined["Path"]);
        Assert.Equal(@"C:\Users\someone\AppData\Local\Temp", combined["TEMP"]);
        Assert.Equal("Windows_NT", combined["OS"]);
    }

    [Fact]
    public void MergePath_AppendsWhatTheRegistryGainedAndDropsOnlyWhatItLost()
    {
        // The process started with a folder its launcher put first, and before Claude Code's installer added its own.
        const string process = @"C:\Tools\launcher;C:\Windows\system32;C:\Old\tool\";
        const string before = @"C:\Windows\system32;C:\Old\tool";
        const string after = @"C:\WINDOWS\System32\;C:\Users\someone\.local\bin";
        Assert.Equal(@"C:\Tools\launcher;C:\Windows\system32;C:\Users\someone\.local\bin", PersistentEnvironment.MergePath(process, before, after));
        // A first read drops nothing: a folder of the process's own is not the registry's to remove.
        Assert.Equal(@"C:\Tools\launcher;C:\Windows\system32;C:\Old\tool\;C:\Users\someone\.local\bin", PersistentEnvironment.MergePath(process, null, after));
        Assert.Equal(@"C:\a;""C:\b c""", PersistentEnvironment.MergePath(@"C:\a;""C:\b c""", null, @"C:\b c\"));
        // Nothing gained or lost: the process's Path as it was, its empty entries too, so nothing is set or logged.
        Assert.Equal(@"C:\a;;C:\b;", PersistentEnvironment.MergePath(@"C:\a;;C:\b;", @"C:\a", @"C:\a;C:\b"));
        Assert.Empty(PersistentEnvironment.Changes(name => name == "Path" ? @"C:\a;;C:\b;" : null, null, Values(("Path", @"C:\b;C:\a"))));
        Assert.Equal([@"C:\Users\someone\.local\bin"], PersistentEnvironment.AddedFolders(before, after));
    }

    [Fact]
    public void Changes_FollowTheRegistryButKeepWhatTheLauncherSet()
    {
        var process = Values(("Path", @"C:\Windows\system32"), ("CODEX_HOME", @"D:\scratch\codex"), ("CLAUDE_CONFIG_DIR", @"C:\Users\someone\.claude-old"), ("GONE", "1"));
        var before = Values(("Path", @"C:\Windows\system32"), ("CODEX_HOME", @"C:\Users\someone\.codex"), ("CLAUDE_CONFIG_DIR", @"C:\Users\someone\.claude-old"), ("GONE", "1"));
        var after = Values(("Path", @"C:\Windows\system32;C:\Users\someone\.local\bin"), ("CODEX_HOME", @"C:\Users\someone\.codex2"),
            ("CLAUDE_CONFIG_DIR", @"C:\Users\someone\.claude"), ("HERMES_HOME", @"C:\hermes"));
        var changes = PersistentEnvironment.Changes(name => process.GetValueOrDefault(name), before, after);
        Assert.Equal(@"C:\Windows\system32;C:\Users\someone\.local\bin", changes["PATH"]);
        Assert.Equal(@"C:\Users\someone\.claude", changes["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(@"C:\hermes", changes["HERMES_HOME"]);
        Assert.True(changes.ContainsKey("GONE") && changes["GONE"] is null);
        // The smoke test's own CODEX_HOME, say: not the registry's, so a change there does not reach it.
        Assert.False(changes.ContainsKey("CODEX_HOME"));
        Assert.Equal(4, changes.Count);
    }

    [Fact]
    public void Changes_OnAFirstReadOnlyAddWhatTheProcessLacks()
    {
        var process = Values(("Path", @"C:\Windows\system32;C:\Users\someone\.local\bin"), ("TEMP", @"C:\Users\SOMEON~1\AppData\Local\Temp"));
        var registry = Values(("Path", @"C:\Windows\system32;C:\Users\someone\.local\bin\"), ("TEMP", @"C:\Users\someone\AppData\Local\Temp"), ("NEW", "x"));
        var changes = PersistentEnvironment.Changes(name => process.GetValueOrDefault(name), null, registry);
        Assert.Equal(["NEW"], changes.Keys);
        Assert.Empty(PersistentEnvironment.Changes(name => name == "NEW" ? "x" : process.GetValueOrDefault(name), registry, registry));
    }
}
