using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PowerShellLocatorTests
{
    private const string ProgramFiles = @"C:\Program Files";
    private const string Msi = @"C:\Program Files\PowerShell\7\pwsh.exe";
    private const string MsixAlias = @"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\pwsh.exe";
    private const string Zip = @"D:\tools\pwsh\pwsh.exe";
    private const string Path = @"C:\Windows\system32;""C:\Users\someone\AppData\Local\Microsoft\WindowsApps"";D:\tools\pwsh";

    private static Func<string, bool> Present(params string[] files) => file => files.Contains(file, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FindPowerShell7_TakesTheMsiFolderFirst()
        => Assert.Equal(Msi, PowerShellLocator.FindPowerShell7(Path, ProgramFiles, Present(Msi, MsixAlias, Zip)));

    [Fact]
    public void FindPowerShell7_FindsTheMsixAliasOnPathWithoutAnMsi()
        // 7.7 has no MSI: the Store's or winget's MSIX is reached through its alias, in a quoted PATH folder here.
        => Assert.Equal(MsixAlias, PowerShellLocator.FindPowerShell7(Path, ProgramFiles, Present(MsixAlias, Zip)));

    [Fact]
    public void FindPowerShell7_FindsAZipInstallOnPath()
        => Assert.Equal(Zip, PowerShellLocator.FindPowerShell7(Path, ProgramFiles, Present(Zip)));

    [Fact]
    public void FindPowerShell7_IsNullWithoutOne()
        => Assert.Null(PowerShellLocator.FindPowerShell7(Path + @";;  ;C:\bad|folder", ProgramFiles, Present(@"C:\Windows\system32\powershell.exe")));

    [Fact]
    public void WindowsPowerShell_IsSystem32s()
        => Assert.Equal(System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), PowerShellLocator.WindowsPowerShell);
}
