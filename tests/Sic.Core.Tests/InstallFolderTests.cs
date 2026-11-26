using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class InstallFolderTests
{
    [Fact]
    public void AFolderThisUserOwnsIsWritableAndKeepsNothingFromTheProbe()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sic-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.True(InstallFolder.IsWritable(folder));
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void AMissingOrBlankFolderIsNotWritable()
    {
        Assert.False(InstallFolder.IsWritable(Path.Combine(Path.GetTempPath(), "sic-missing-" + Guid.NewGuid().ToString("N"))));
        Assert.False(InstallFolder.IsWritable(""));
    }

    [Fact]
    public void TheWindowsFolderIsNotWritableWithoutElevation()
    {
        // A test run is not elevated, as SicTerm is not; this is the Program Files case without an install to try it on.
        if (Environment.IsPrivilegedProcess) return;
        Assert.False(InstallFolder.IsWritable(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
    }
}
