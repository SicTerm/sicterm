using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sic.Core.Agents;

namespace Sic.App;

/// <summary>
/// What PowerShell resolved an agent's command to. <see cref="Kind"/> is the command type (Application, Alias, Function, Cmdlet,
/// ExternalScript), null when nothing answered. <see cref="Path"/> is the file an Application or ExternalScript is, or the one an
/// alias resolves to; <see cref="Definition"/> an alias's text or the file a function was read from; <see cref="Module"/> the module
/// a function or cmdlet came from. A command whose file is gone, an alias to one, or a function with no program of its name on
/// PATH behind it is not <see cref="Available"/>: a name that answers in PowerShell is not an installed tool until its file is
/// there. Hermes's installer writes such a function into the PowerShell profile, which a synced profile carries to a PC where
/// Hermes was never installed (seen October 16, 2026).
/// </summary>
internal sealed record DetectedAgent(string Id, bool Available, string? Path, string? Kind = null, string? Definition = null, string? Module = null)
{
    /// <summary>The found-at line of the agent's card in Settings: what the command is and where, or why it does not count.</summary>
    internal string Describe()
    {
        var module = Module is { Length: > 0 } ? Module : null;
        // Where a function was read from: its module, the file (the profile, as a rule), or the profile when PowerShell kept no file.
        var function = module is not null ? "A function from the " + module + " module" : "A function in " + (Definition is { Length: > 0 } ? Definition : "your PowerShell profile");
        if (!Available)
        {
            if (Kind == "Alias" && Definition is { Length: > 0 }) return "An alias in your PowerShell profile for " + Definition + ", which does not exist. Install it, or set its executable below.";
            if (Kind is "Function" or "Filter") return function + ", but no program of that name on PATH for it to run. Install it, or set its executable below.";
            if (Path is { Length: > 0 }) return "Resolves to " + Path + ", which does not exist. Install it, or set its executable below.";
            return "Not found on PATH or in your PowerShell profile. Install it, or set its executable below.";
        }
        return Kind switch
        {
            "Alias" => "An alias in your PowerShell profile for " + (Path ?? Definition ?? "another command"),
            "Function" or "Filter" => function + (Path is { Length: > 0 } ? " that runs " + Path : ""),
            "Cmdlet" => module is not null ? "A cmdlet from the " + module + " module" : "A cmdlet",
            _ => Path is { Length: > 0 } ? "Found at " + Path : "Found through your PowerShell profile."
        };
    }

    /// <summary>One line for sic.log.</summary>
    internal string Summary() => Id + "=" + (Available ? "found" : "missing") + (Kind is null ? "" : " " + Kind) + (Path is null ? "" : " " + Path)
        + (Definition is null ? "" : " → " + Definition) + (Module is { Length: > 0 } ? " [" + Module + "]" : "");
}

internal static class AgentDetection
{
    internal static string PowerShellPath
    {
        get
        {
            var standard = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            if (File.Exists(standard)) return standard;
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var path = System.IO.Path.Combine(folder.Trim('"'), "pwsh.exe");
                if (File.Exists(path)) return System.IO.Path.GetFullPath(path);
            }
            throw new FileNotFoundException("PowerShell 7 (pwsh.exe) was not found.");
        }
    }

    internal static async Task<IReadOnlyList<DetectedAgent>> ProbeAsync(IEnumerable<AgentDefinition> agents)
    {
        static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        var definitions = string.Join(",", agents.Select(agent => "@{Id=" + Quote(agent.Id) + ";Command=" + Quote(agent.Executable) + "}"));
        var marker = "SIC_" + Guid.NewGuid().ToString("N") + ":";
        // The command as PowerShell resolves it, with the user's profile loaded, so a tool wrapped in a profile function counts.
        // An alias is followed to its target; a file an Application, a script or an alias names must exist, or the tool is not
        // there (a stale alias or a PATH stub answers Get-Command and would otherwise pass for an install).
        var script = "$rows=@(foreach($definition in @(" + definitions + ")) { " +
            "$command=Get-Command -Name $definition.Command -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            "$program=Get-Command -Name $definition.Command -CommandType Application,ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            "$target=$command; if ($command -is [System.Management.Automation.AliasInfo]) { $target=$command.ResolvedCommand }; " +
            // A function is a wrapper (Hermes's installer writes one into the profile that runs the hermes on PATH): the program
            // of the same name is what counts, and a function with no program behind it is a leftover, not an install.
            "if ($null -ne $target -and ($target.CommandType -in 'Function','Filter') -and ($null -ne $program)) { $target=$program }; " +
            "$path=if ($null -ne $target -and ($target.CommandType -in 'Application','ExternalScript')) { [string]$target.Path } else { $null }; " +
            "$available=($null -ne $target) -and ($target.CommandType -notin 'Function','Filter') -and (($null -eq $path) -or (Test-Path -LiteralPath $path -PathType Leaf)); " +
            "$definitionText=if ($command -is [System.Management.Automation.AliasInfo]) { [string]$command.Definition } elseif ($command -is [System.Management.Automation.FunctionInfo]) { [string]$command.ScriptBlock.File } else { $null }; " +
            "[pscustomobject]@{Id=$definition.Id;Available=$available;Path=$path;Kind=$(if ($null -ne $command) { [string]$command.CommandType } else { $null });" +
            "Definition=$definitionText;Module=$(if ($null -ne $command) { [string]$command.ModuleName } else { $null })} }); " +
            "[Console]::WriteLine(" + Quote(marker) + "+(ConvertTo-Json -InputObject $rows -Compress))";
        var start = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start tool detection.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("Tool detection timed out after 20 seconds."); }
        var text = await output;
        await errors;
        var line = text.Split('\n').LastOrDefault(value => value.StartsWith(marker, StringComparison.Ordinal));
        if (process.ExitCode != 0 || line is null) throw new InvalidOperationException("Tool detection returned no usable result from the commands in Settings.");
        return JsonSerializer.Deserialize<List<DetectedAgent>>(line[marker.Length..], new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
