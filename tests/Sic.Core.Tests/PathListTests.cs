using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PathListTests
{
    [Fact]
    public void Find_TakesTheFirstFolderThatHoldsTheFile()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\tools\npm.cmd", @"D:\later\npm.cmd" };
        Assert.Equal(@"C:\tools\npm.cmd", PathList.Find("npm.cmd", @"C:\empty;""C:\tools"";D:\later", files.Contains));
        Assert.Null(PathList.Find("winget.exe", @"C:\empty;C:\tools", files.Contains));
        Assert.Null(PathList.Find("npm.cmd", "", files.Contains));
    }

    [Theory]
    [InlineData(@"C:\a;C:\Users\someone\.local\bin", true)]
    [InlineData(@"C:\a;c:\users\SOMEONE\.local\bin\", true)]
    [InlineData(@"C:\a;""C:\Users\someone\.local\bin""", true)]
    [InlineData(@"C:\a; C:\Users\someone\.local\bin ;C:\b", true)]
    [InlineData(@"C:\a;C:\Users\someone\.local\bin2", false)]
    [InlineData(@"C:\a;C:\Users\someone\.local", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Contains_ComparesFoldersWithoutCaseQuotesOrATrailingBackslash(string? path, bool expected)
        => Assert.Equal(expected, PathList.Contains(path, @"C:\Users\someone\.local\bin"));

    [Fact]
    public void Contains_ExpandsAVariableInAnEntry()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
        Assert.True(PathList.Contains(@"C:\a;%USERPROFILE%\.local\bin", folder));
    }

    [Fact]
    public void Append_AddsTheFolderLastAndLeavesEveryOtherEntryAsWritten()
    {
        const string folder = @"C:\Users\someone\.local\bin";
        Assert.Equal(@"%USERPROFILE%\go\bin;C:\tools;" + folder, PathList.Append(@"%USERPROFILE%\go\bin;C:\tools", folder));
        // One separator between the old end and the new folder, whatever the value ended with.
        Assert.Equal(@"C:\tools;" + folder, PathList.Append(@"C:\tools;", folder));
        Assert.Equal(folder, PathList.Append("", folder));
        Assert.Equal(folder, PathList.Append(null, folder));
        // Already there, however it is spelled: nothing to write.
        Assert.Null(PathList.Append(@"C:\tools;c:\users\someone\.local\bin\", folder));
    }

    [Theory]
    [InlineData("PS C:\\Users\\someone>", true)]
    [InlineData("Windows PowerShell\nCopyright (C) Microsoft Corporation.\n\nPS C:\\Users\\someone> \n\n\n", true)]
    [InlineData("PS C:\\Users\\someone> winget install", false)]
    [InlineData("Loading personal and system profiles took 1200ms.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShellPromptReady_IsPowerShellsPromptWithNothingTypedAfterIt(string? screen, bool expected)
        => Assert.Equal(expected, ShellPrompt.Ready(screen));
}
