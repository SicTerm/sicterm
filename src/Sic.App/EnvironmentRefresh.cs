using System.Collections;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// Keeps SicTerm's own variables in step with the ones Windows keeps for the user and the PC (<see cref="PersistentEnvironment"/>),
/// so a terminal started, or a detection run, after an installer changed them sees the change. Run before each detection and
/// each terminal start, and when Windows says the environment changed (WM_SETTINGCHANGE "Environment", MainWindow.cs); a read
/// of the registry is a few milliseconds. Not thread-safe: SicTerm calls it from the UI thread only.
/// </summary>
internal static class EnvironmentRefresh
{
    private static Dictionary<string, string>? _lastRead;

    /// <summary>Applies what changed since the last read, or on the first, what the process lacks. Never throws: a failed read is logged and changes nothing.</summary>
    internal static void Apply()
    {
        try
        {
            var now = PersistentEnvironment.Combine(Read(EnvironmentVariableTarget.Machine), Read(EnvironmentVariableTarget.User));
            var changes = PersistentEnvironment.Changes(Environment.GetEnvironmentVariable, _lastRead, now);
            var path = Environment.GetEnvironmentVariable("Path");
            foreach (var (name, value) in changes) Environment.SetEnvironmentVariable(name, value);
            _lastRead = now;
            // Names only, and Path's new folders: a value can be a key or a token.
            if (changes.Count > 0) Diagnostics.Log("environment from the registry: " + string.Join("; ", changes.Select(change => Describe(change.Key, change.Value, path))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("environment from the registry: " + exception.Message); }
        AddClaudeFolder();
    }

    /// <summary>
    /// Claude Code's own installer puts claude.exe under the profile's .local\bin and leaves that folder off Path (2.1.332; a
    /// Windows Sandbox run, November 27, 2026), so a fresh install was not found. Where the program is there and Path does not
    /// name its folder, SicTerm's own Path gets it, last: detection and every tab then find claude, and nothing outside SicTerm
    /// changes (the account's Path is the row's Add to PATH, <see cref="UserPath"/>).
    /// </summary>
    private static void AddClaudeFolder()
    {
        try
        {
            var folder = SetupCatalog.ClaudeFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            var path = Environment.GetEnvironmentVariable("Path");
            if (!File.Exists(Path.Combine(folder, "claude.exe")) || PathList.Append(path, folder) is not { } added) return;
            Environment.SetEnvironmentVariable("Path", added);
            Diagnostics.Log("environment: Path gained " + folder + " for SicTerm alone (Claude Code's installer left it off)");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Diagnostics.Log("environment, Claude Code's folder: " + exception.Message); }
    }

    private static string Describe(string name, string? value, string? path)
    {
        if (!string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase)) return name + (value is null ? " removed" : " set");
        var added = PersistentEnvironment.AddedFolders(path, value).ToList();
        return added.Count == 0 ? "Path lost folders" : "Path gained " + string.Join(", ", added);
    }

    /// <summary>One registry's variables, expanded as a logon expands them.</summary>
    private static Dictionary<string, string> Read(EnvironmentVariableTarget target)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables(target))
            if (entry.Key is string name && entry.Value is string value) values[name] = Environment.ExpandEnvironmentVariables(value);
        return values;
    }
}
