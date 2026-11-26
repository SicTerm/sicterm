using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class TrayPlacementTests
{
    private static readonly Guid ProgramFiles = Guid.Parse("6D809377-6AF0-444B-8957-A3773F02200E");
    private static string? KnownFolder(Guid id) => id == ProgramFiles ? @"C:\Program Files" : null;

    [Fact]
    public void FindsTheEntryForThisExeAndIconId()
    {
        var entries = new[]
        {
            new TrayPlacement.Entry("1", @"C:\Users\Avery\AppData\Local\Microsoft\OneDrive\OneDrive.exe", 1, false, 1),
            new TrayPlacement.Entry("2", @"C:\Users\Avery\AppData\Local\SicTerm\current\SicTerm.exe", 2, false, null),
            new TrayPlacement.Entry("3", @"c:\users\avery\appdata\local\SICTERM\current\SicTerm.exe", 1, false, null),
        };
        Assert.Equal("3", TrayPlacement.Find(entries, @"C:\Users\Avery\AppData\Local\SicTerm\current\SicTerm.exe", 1, KnownFolder)?.Name);
    }

    [Fact]
    public void AKnownFolderIdStandsForItsPath()
    {
        var entries = new[] { new TrayPlacement.Entry("7", @"{6D809377-6AF0-444B-8957-A3773F02200E}\SicTerm\SicTerm.exe", 1, false, null) };
        Assert.Equal("7", TrayPlacement.Find(entries, @"C:\Program Files\SicTerm\SicTerm.exe", 1, KnownFolder)?.Name);
        Assert.Null(TrayPlacement.Expand(@"{00000000-0000-0000-0000-000000000001}\SicTerm.exe", KnownFolder));
        Assert.Null(TrayPlacement.Expand(@"{not a guid}\SicTerm.exe", KnownFolder));
    }

    [Fact]
    public void AnotherCopyOrAGuidIconIsNotThisOne()
    {
        var entries = new[]
        {
            new TrayPlacement.Entry("1", @"D:\Repos\sic\artifacts\SicTerm\SicTerm.exe", 1, false, null),
            new TrayPlacement.Entry("2", @"C:\Users\Avery\AppData\Local\SicTerm\current\SicTerm.exe", null, true, null),
            new TrayPlacement.Entry("3", null, 1, false, null),
        };
        Assert.Null(TrayPlacement.Find(entries, @"C:\Users\Avery\AppData\Local\SicTerm\current\SicTerm.exe", 1, KnownFolder));
    }

    [Fact]
    public void OnlyAnUnplacedIconIsPromoted()
    {
        Assert.True(TrayPlacement.ShouldPromote(new TrayPlacement.Entry("1", @"C:\a.exe", 1, false, null)));
        // 1: on the taskbar already. 0: the user hid it, and it stays hidden.
        Assert.False(TrayPlacement.ShouldPromote(new TrayPlacement.Entry("1", @"C:\a.exe", 1, false, 1)));
        Assert.False(TrayPlacement.ShouldPromote(new TrayPlacement.Entry("1", @"C:\a.exe", 1, false, 0)));
    }
}
