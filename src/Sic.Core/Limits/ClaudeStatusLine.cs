using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sic.Core.Limits;

/// <summary>
/// How SIC reads Claude's plan limits without touching a credential or an agent file (docs/decisions/usage-limits.md). Claude
/// Code hands its status line command a JSON description of the session after each reply, plan windows included. When SIC
/// starts Claude Code it adds <c>--settings &lt;file&gt;</c> naming SicTermStatusLine.exe, beside SicTerm.exe, as that command
/// (<see cref="SettingsJson"/>); command-line settings win over the user's, project and local ones, and nothing in them is
/// changed. The helper keeps the windows in the data folder (<see cref="Handle"/>) and runs the status line the user had, if
/// any, with the same input, printing what it prints, so the user's own line shows as before.
/// </summary>
public static class ClaudeStatusLine
{
    /// <summary>The helper's file name, beside SicTerm.exe; also how SIC's own status line is told from the user's.</summary>
    public const string ExecutableName = "SicTermStatusLine.exe";
    /// <summary>The helper's name before the rename to SicTerm (October 13, 2026), still told apart as SIC's own in a copied settings file.</summary>
    public const string FormerExecutableName = "SIChatStatusLine.exe";
    /// <summary>The kept reading, under the data folder's limits folder.</summary>
    public const string ReadingFile = "claude.json";
    /// <summary>How long the user's own status line may take; Claude Code cancels a slower one when the next update comes anyway.</summary>
    public static readonly TimeSpan UserCommandTimeout = TimeSpan.FromSeconds(10);

    public static string LimitsFolder(string dataDirectory) => Path.Combine(dataDirectory, "limits");

    /// <summary>Where each session's context-window reading is kept (<see cref="ContextWindow"/>): one file per session under the limits folder.</summary>
    public static string ContextFolder(string dataDirectory) => Path.Combine(LimitsFolder(dataDirectory), "context");

    /// <summary>The kept reading's file for a session, or null for a session ID that is not a Claude Code one (the ID names the file, so only an exact ID is written).</summary>
    public static string? ContextFile(string dataDirectory, string sessionId) =>
        Agents.AgentLaunchBuilder.IsExactSessionId("claude", sessionId) ? Path.Combine(ContextFolder(dataDirectory), sessionId.ToLowerInvariant() + ".json") : null;

    /// <summary>The session ID a context file is named for, or null for another file.</summary>
    public static string? ContextSessionOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Agents.AgentLaunchBuilder.IsExactSessionId("claude", name) ? name : null;
    }

    /// <summary>The kept context reading of a session, or null.</summary>
    public static ContextReading? ReadContext(string dataDirectory, string sessionId)
    {
        if (ContextFile(dataDirectory, sessionId) is not { } path) return null;
        try { return ContextWindow.Deserialize(File.ReadAllText(path)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Removes context readings not written for <paramref name="keep"/>: a session's file outlives the session, and nothing else clears them.</summary>
    public static void PruneContext(string dataDirectory, TimeSpan keep, DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(ContextFolder(dataDirectory))) return;
            foreach (var file in new DirectoryInfo(ContextFolder(dataDirectory)).EnumerateFiles("*.json"))
                if (now - file.LastWriteTimeUtc > keep) { try { file.Delete(); } catch (IOException) { } }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The command Claude Code runs, through Git Bash when it is installed and PowerShell otherwise: the helper's path with
    /// forward slashes, as Claude Code's own Windows example writes one, which both shells take unquoted; in its short form when
    /// it has a space, since a quoted path followed by arguments is not a command in PowerShell. The helper is a console program,
    /// so either shell hands it its input and waits for it. The data folder is named when it is not the default one, as in the smoke.
    /// </summary>
    public static string Command(string executable, string? dataDirectory, Func<string, string>? shortPath = null)
    {
        static string Plain(string path, Func<string, string>? shortPath) =>
            (path.Contains(' ') && shortPath is not null ? shortPath(path) : path).Replace('\\', '/');
        var command = Plain(executable, shortPath);
        return dataDirectory is null ? command : command + " --data-dir " + Plain(dataDirectory, shortPath);
    }

    /// <summary>
    /// The helper's whole run: <c>--data-dir &lt;folder&gt;</c> or <paramref name="defaultDataDirectory"/>, the session's JSON read
    /// from <paramref name="input"/>, and the user's own line written to <paramref name="output"/>. Nothing here may fail Claude
    /// Code's interface, so any error ends it with nothing printed; the next update tries again.
    /// </summary>
    public static int Run(string[] arguments, Stream input, Stream output, string defaultDataDirectory)
    {
        try
        {
            var data = arguments.Length >= 2 && arguments[0] == "--data-dir" ? Path.GetFullPath(arguments[1]) : defaultDataDirectory;
            var utf8 = new UTF8Encoding(false);
            string text;
            using (var reader = new StreamReader(input, utf8)) text = reader.ReadToEnd();
            var printed = Handle(text, data, DateTimeOffset.UtcNow, RunUserCommand, PaneOrigin.Current());
            if (printed.Length > 0) { output.Write(utf8.GetBytes(printed)); output.Flush(); }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The settings SIC hands Claude Code: the user's effective status line kept field for field (padding, refresh interval and
    /// the rest), with its command replaced by SIC's. The user's command is not in it; <see cref="Handle"/> finds that itself.
    /// </summary>
    public static string SettingsJson(string command, JsonObject? userStatusLine)
    {
        var line = userStatusLine?.DeepClone().AsObject() ?? new JsonObject();
        line["type"] = "command";
        line["command"] = command;
        return new JsonObject { ["statusLine"] = line }.ToJsonString();
    }

    /// <summary>
    /// Writes <see cref="SettingsJson"/> to the limits folder under a name taken from its content, so panes with the same
    /// settings share a file and none is rewritten under a running Claude Code. Returns the file's path.
    /// </summary>
    public static string WriteSettings(string dataDirectory, string json)
    {
        var folder = LimitsFolder(dataDirectory);
        Directory.CreateDirectory(folder);
        var name = "claude-settings-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16].ToLowerInvariant() + ".json";
        var path = Path.Combine(folder, name);
        if (!File.Exists(path)) WriteAtomically(path, json);
        return path;
    }

    /// <summary>
    /// The status line the user would have in <paramref name="workingDirectory"/>: the last of the user's, the project's and the
    /// project's local settings files that sets one, as Claude Code ranks them. Null when none does, or when the one found is
    /// SIC's own (a settings file copied from a SIC launch).
    /// </summary>
    public static JsonObject? UserStatusLine(string claudeHome, string? workingDirectory)
    {
        var files = new List<string> { Path.Combine(claudeHome, "settings.json") };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            files.Add(Path.Combine(workingDirectory, ".claude", "settings.json"));
            files.Add(Path.Combine(workingDirectory, ".claude", "settings.local.json"));
        }
        JsonObject? found = null;
        foreach (var file in files)
        {
            try
            {
                if (!File.Exists(file)) continue;
                var node = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (node is JsonObject settings && settings["statusLine"] is JsonObject line) found = line;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { /* A file Claude Code could not read either. */ }
        }
        return found is not null && CommandOf(found) is { } command && !IsSicsOwn(command) ? found : null;
    }

    private static bool IsSicsOwn(string command) => new[] { ExecutableName, FormerExecutableName }
        .Any(name => command.Contains(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase));

    private static string? CommandOf(JsonObject line) => line["command"] is JsonValue value && value.TryGetValue<string>(out var command) ? command : null;

    /// <summary>Claude Code's home for its settings: CLAUDE_CONFIG_DIR when set, as SIC sets it for a pane with another home.</summary>
    public static string Home() =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } home ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>
    /// What SicTerm.exe does as the status line: keeps the readings in <paramref name="input"/>, when it has them, in the data
    /// folder (the plan's windows in one file, the session's context window in a file of its own), then runs the user's own
    /// status line command with the same input and returns its output, or "" when there is none. A failure to keep a reading
    /// never stops the user's line. The context reading carries <paramref name="origin"/>, the pane and the Claude Code process
    /// it came from (<see cref="PaneOrigin.Current"/>).
    /// </summary>
    public static string Handle(string input, string dataDirectory, DateTimeOffset now, Func<string, string, string?> runUserCommand,
        (string? Pane, int? AgentProcess) origin = default)
    {
        try
        {
            if (UsageLimits.FromClaudeStatusLine(input, now) is { } reading)
            {
                var folder = LimitsFolder(dataDirectory);
                Directory.CreateDirectory(folder);
                WriteAtomically(Path.Combine(folder, ReadingFile), UsageLimits.Serialize(reading));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* The next update writes it. */ }
        try
        {
            if (ContextWindow.FromClaudeStatusLine(input, now) is { } context && ContextFile(dataDirectory, context.SessionId) is { } file)
            {
                Directory.CreateDirectory(ContextFolder(dataDirectory));
                WriteAtomically(file, ContextWindow.Serialize(context with { Pane = origin.Pane, AgentProcess = origin.AgentProcess }));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* The next update writes it. */ }
        var command = UserStatusLine(Home(), WorkingDirectory(input)) is { } line ? CommandOf(line) : null;
        return string.IsNullOrWhiteSpace(command) ? "" : runUserCommand(command, input) ?? "";
    }

    /// <summary>The session's folder from the status line input: <c>workspace.current_dir</c>, else <c>cwd</c>.</summary>
    public static string? WorkingDirectory(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("workspace", out var workspace) && workspace.ValueKind == JsonValueKind.Object
                && workspace.TryGetProperty("current_dir", out var current) && current.ValueKind == JsonValueKind.String) return current.GetString();
            return root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String ? cwd.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Runs the user's status line command as Claude Code would: through Git Bash when it is installed (CLAUDE_CODE_GIT_BASH_PATH,
    /// or the bash.exe beside git.exe on the PATH), otherwise through PowerShell. Returns its standard output, or null when it
    /// could not run or did not finish within <see cref="UserCommandTimeout"/>.
    /// </summary>
    public static string? RunUserCommand(string command, string input)
    {
        var bash = GitBash();
        var start = bash is not null
            ? new ProcessStartInfo(bash) { ArgumentList = { "-c", command } }
            : new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", command } };
        start.RedirectStandardInput = start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            if (!process.WaitForExit(UserCommandTimeout)) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return null; }
            return output.Result;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }

    /// <summary>The Git Bash Claude Code runs status lines through, found as it finds it; null when there is none, and PowerShell runs them.</summary>
    public static string? GitBash()
    {
        if (Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } configured && File.Exists(configured)) return configured;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!File.Exists(Path.Combine(folder, "git.exe"))) continue;
                // Git for Windows keeps git.exe in cmd\ (and bin\), and bash.exe in bin\.
                var root = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\'));
                var bash = root is null ? null : Path.Combine(root, "bin", "bash.exe");
                if (bash is not null && File.Exists(bash)) return bash;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException) { }
        }
        return null;
    }

    private static void WriteAtomically(string path, string text)
    {
        var temporary = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The kept Claude reading in the data folder, or null.</summary>
    public static LimitReading? Read(string dataDirectory)
    {
        try { return UsageLimits.Deserialize(File.ReadAllText(Path.Combine(LimitsFolder(dataDirectory), ReadingFile))); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Keeps a Claude reading from another source (Hermes's usage command) as the status line keeps its own, replacing the file whole.</summary>
    public static void Write(string dataDirectory, LimitReading reading) => WriteReading(dataDirectory, ReadingFile, reading);

    /// <summary>Keeps a plan's reading in the limits folder under <paramref name="fileName"/>, written whole and moved into place.</summary>
    public static void WriteReading(string dataDirectory, string fileName, LimitReading reading)
    {
        var folder = LimitsFolder(dataDirectory);
        Directory.CreateDirectory(folder);
        WriteAtomically(Path.Combine(folder, fileName), UsageLimits.Serialize(reading));
    }
}
