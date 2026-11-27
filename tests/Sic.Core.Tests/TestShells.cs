using Sic.Core.Shell;

namespace Sic.Core.Tests;

/// <summary>The shells the tests that start a real PowerShell run in.</summary>
internal static class TestShells
{
    /// <summary>
    /// COMMAND_CENTRAL_PWSH when it names a shell (Windows PowerShell 5.1, or the MSIX's alias, to test those), otherwise
    /// PowerShell 7 wherever it is installed, found as SicTerm finds it: the MSI's Program Files folder, then PATH.
    /// </summary>
    internal static string PowerShell { get; } = Environment.GetEnvironmentVariable("COMMAND_CENTRAL_PWSH") is { Length: > 0 } named ? named
        : PowerShellLocator.FindPowerShell7()
          ?? throw new InvalidOperationException("PowerShell 7 (pwsh.exe) was not found under Program Files or on PATH, and COMMAND_CENTRAL_PWSH names no shell; the terminal tests start a real PowerShell.");

    internal static string WindowsPowerShell => PowerShellLocator.WindowsPowerShell;
}
