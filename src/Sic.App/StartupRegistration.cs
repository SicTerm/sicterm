using Microsoft.Win32;
using Sic.Core.Shell;

namespace Sic.App;

/// <summary>
/// The registry side of Settings → General → Run at Startup (<see cref="RunAtStartup"/>): one value under the person's Run key.
/// <paramref name="name"/> is the value's name everywhere, SicTerm's own unless a check writes a scratch one.
/// </summary>
internal static class StartupRegistration
{
    /// <summary>The command kept under the name, or null for none.</summary>
    internal static string? Command(string name = RunAtStartup.ValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunAtStartup.RunKey);
        return key?.GetValue(name) as string;
    }

    /// <summary>True when the entry starts <paramref name="executable"/> and has not been turned off in Task Manager.</summary>
    internal static bool IsOn(string executable, string name = RunAtStartup.ValueName)
    {
        using var approved = Registry.CurrentUser.OpenSubKey(RunAtStartup.ApprovedKey);
        return RunAtStartup.IsOn(Command(name), approved?.GetValue(name) as byte[], executable);
    }

    /// <summary>Starts <paramref name="executable"/> at sign-in, clearing a Disabled left by Task Manager so that on means on.</summary>
    internal static void Enable(string executable, string name = RunAtStartup.ValueName)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunAtStartup.RunKey, writable: true) ?? throw new InvalidOperationException("The registry key could not be opened: " + RunAtStartup.RunKey))
            key.SetValue(name, RunAtStartup.Command(executable));
        ClearApproval(name);
    }

    /// <summary>Removes the entry, whichever copy it started.</summary>
    internal static void Disable(string name = RunAtStartup.ValueName)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunAtStartup.RunKey, writable: true))
            key?.DeleteValue(name, throwOnMissingValue: false);
        ClearApproval(name);
    }

    private static void ClearApproval(string name)
    {
        using var approved = Registry.CurrentUser.OpenSubKey(RunAtStartup.ApprovedKey, writable: true);
        approved?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>An uninstall takes this copy's entry with it; another copy's stays.</summary>
    internal static void TryRemoveInstalled()
    {
        try
        {
            if (Environment.ProcessPath is { } executable && string.Equals(RunAtStartup.ExecutableOf(Command()), executable, StringComparison.OrdinalIgnoreCase)) Disable();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* The uninstall goes on. */ }
    }
}
