using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class RunAtStartupTests
{
    private const string Exe = @"C:\Users\Someone\AppData\Local\SicTerm\current\SicTerm.exe";

    [Fact]
    public void TheCommandIsTheExeQuotedAndReadsBack()
    {
        Assert.Equal("\"" + Exe + "\"", RunAtStartup.Command(Exe));
        Assert.Equal(Exe, RunAtStartup.ExecutableOf(RunAtStartup.Command(Exe)));
        Assert.Equal(Exe, RunAtStartup.ExecutableOf("\"" + Exe + "\" --tray"));
        Assert.Equal(@"C:\Tools\SicTerm.exe", RunAtStartup.ExecutableOf(@"C:\Tools\SicTerm.exe --tray"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unclosed")]
    [InlineData("\"\"")]
    public void AValueThatIsNoCommandHasNoExe(string? command) => Assert.Null(RunAtStartup.ExecutableOf(command));

    [Fact]
    public void WindowsRunsAnEntryUnlessTaskManagerTurnedItOff()
    {
        Assert.True(RunAtStartup.Approved(null));
        Assert.True(RunAtStartup.Approved([]));
        Assert.True(RunAtStartup.Approved([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.False(RunAtStartup.Approved([3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8]));
    }

    [Fact]
    public void OnOnlyForThisExeAndOnlyWhileWindowsRunsIt()
    {
        var command = RunAtStartup.Command(Exe);
        Assert.True(RunAtStartup.IsOn(command, null, Exe.ToUpperInvariant()));
        Assert.False(RunAtStartup.IsOn(command, [3], Exe));
        Assert.False(RunAtStartup.IsOn(command, null, @"C:\Elsewhere\SicTerm.exe"));
        Assert.False(RunAtStartup.IsOn(null, null, Exe));
    }
}
