using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class PathPasteTests
{
    [Theory]
    [InlineData(@"C:\Batch\Repos\sic", @"C:\Batch\Repos\sic")]
    [InlineData(@"C:\Program Files", @"'C:\Program Files'")]
    [InlineData(@"C:\Users\o'brien\notes.md", @"'C:\Users\o''brien\notes.md'")]
    [InlineData(@"C:\temp\$env:PATH", @"'C:\temp\$env:PATH'")]
    [InlineData(@"C:\temp\a;b", @"'C:\temp\a;b'")]
    [InlineData("", "''")]
    public void QuoteLeavesPlainPathsAloneAndSingleQuotesTheRest(string path, string expected)
    {
        Assert.Equal(expected, PathPaste.Quote(path));
    }

    [Fact]
    public void JoinQuotesEachPathSeparatesThemWithSpacesAndNeverEndsInEnter()
    {
        var joined = PathPaste.Join([@"C:\Program Files\app.exe", "", null, @"D:\data\report.csv", @"E:\it's here"]);
        Assert.Equal(@"'C:\Program Files\app.exe' D:\data\report.csv 'E:\it''s here'", joined);
        Assert.DoesNotContain('\r', joined);
        Assert.DoesNotContain('\n', joined);
    }

    [Fact]
    public void JoinOfNothingIsEmpty()
    {
        Assert.Equal("", PathPaste.Join([]));
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData("  C:\\Program Files\r\n")]
    [InlineData("\"C:\\Program Files\"")]
    public void TextNamingAnExistingPathIsQuotedLikeADroppedFile(string text)
    {
        Assert.Equal(@"'C:\Program Files'", PathPaste.FromText(text, path => path == @"C:\Program Files"));
    }

    [Fact]
    public void APathThatDoesNotExistGoesInAsItCame()
    {
        Assert.Equal(@"C:\does not\exist", PathPaste.FromText(@"C:\does not\exist", _ => false));
    }

    [Theory]
    [InlineData(@"relative folder\file.txt")]
    [InlineData(@"\rooted but\not qualified")]
    [InlineData(@"C:drive relative")]
    [InlineData("git status")]
    [InlineData("C:\\one two\nC:\\three four")]
    [InlineData("\"C:\\a\" \"C:\\b\"")]
    [InlineData("   ")]
    public void TextThatIsNotOneFullyQualifiedPathGoesInAsItCameEvenIfSomethingByThatNameExists(string text)
    {
        Assert.Equal(text, PathPaste.FromText(text, _ => true));
    }

    [Fact]
    public void TextIsCheckedAgainstTheFileSystemByDefault()
    {
        var folder = Directory.CreateTempSubdirectory("sic path paste ");
        try
        {
            var file = Path.Combine(folder.FullName, "it's.txt");
            File.WriteAllText(file, "");
            Assert.Equal(PathPaste.Quote(folder.FullName), PathPaste.FromText(folder.FullName));
            Assert.Equal(PathPaste.Quote(file), PathPaste.FromText(file));
            Assert.StartsWith("'", PathPaste.FromText(file));
            var missing = Path.Combine(folder.FullName, "missing");
            Assert.Equal(missing, PathPaste.FromText(missing));
        }
        finally { folder.Delete(recursive: true); }
    }
}
