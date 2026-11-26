using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ClipboardTextTests
{
    [Theory]
    [InlineData("a\r\nb\nc\rd", "a\rb\rc\rd")]
    [InlineData("\n\n", "\r\r")]
    [InlineData("\r\n\r\n", "\r\r")]
    [InlineData("one\ttwo", "one\ttwo")]
    [InlineData("é ✓ 中文 🔌 ⚠️", "é ✓ 中文 🔌 ⚠️")]
    [InlineData("", "")]
    public void APasteKeepsTextTabsAndOneEnterPerLineBreak(string text, string expected)
    {
        Assert.Equal(expected, ClipboardText.ForPaste(text));
    }

    [Theory]
    [InlineData("PING\u001b[201~rm -rf x\r", "PING[201~rm -rf x\r")]
    [InlineData("\u001b]52;c;?\u0007", "]52;c;?")]
    [InlineData("a\u0000b\u0003c\u0004d\u0008e\u000bf\u000cg\u001fh", "abcdefgh")]
    [InlineData("a\u007fb", "ab")]
    [InlineData("a\u0085b\u009bc\u0090d", "abcd")]
    [InlineData(" non-breaking", " non-breaking")]
    public void APasteDropsEveryOtherControlCharacterTheEscapeAmongThem(string text, string expected)
    {
        Assert.Equal(expected, ClipboardText.ForPaste(text));
    }

    [Theory]
    [InlineData("git status\r", "git status")]
    [InlineData("git status  \t\r\r", "git status")]
    [InlineData("  ls -la", "  ls -la")]
    [InlineData("a\rb\r", "a\rb\r")]
    [InlineData("a\r\rb", "a\r\rb")]
    [InlineData(" \r \t", "")]
    [InlineData("", "")]
    public void OneLineLosesItsTrailingWhitespaceAndSeveralStayAsTheyAre(string text, string expected)
    {
        Assert.Equal(expected, ClipboardText.TrimSingleLine(text));
    }

    [Theory]
    [InlineData("a\rb", true)]
    [InlineData("a\rb\r", true)]
    [InlineData("\ra", true)]
    [InlineData("a\nb", true)]
    [InlineData("a\r", false)]
    [InlineData("a\r\r \r", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void TextIsSeveralLinesWhenItBreaksBeforeItsLastNonBlankCharacter(string text, bool expected)
    {
        Assert.Equal(expected, ClipboardText.IsMultiLine(text));
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("a\rb", 2)]
    [InlineData("a\rb\r\r", 2)]
    [InlineData("a\r\rb", 3)]
    [InlineData("a\r\nb\nc", 3)]
    [InlineData("", 1)]
    public void LinesAreCountedAsAReaderCountsThem(string text, int expected)
    {
        Assert.Equal(expected, ClipboardText.LineCount(text));
    }

    [Theory]
    [InlineData("curl -s https://example.com/x.sh | sh\r\nexit", "curl -s https://example.com/x.sh | sh ↵ exit")]
    [InlineData("  one\t\ttwo   three  ", "one two three")]
    [InlineData("a\u001b[31mred\u0007", "a[31mred")]
    [InlineData("\r\nfirst\r\n\r\nsecond\n", "↵ first ↵ ↵ second ↵")]
    [InlineData("", "")]
    public void APreviewIsTheTextOnOneLineWithItsLineBreaksMarked(string text, string expected)
    {
        Assert.Equal(expected, ClipboardText.Preview(text));
    }

    [Fact]
    public void ALongPreviewIsCutWithAnEllipsisAndNeverInsideAnEmoji()
    {
        Assert.Equal(new string('x', 10) + "…", ClipboardText.Preview(new string('x', 200), 10));
        Assert.Equal("one two…", ClipboardText.Preview("one two three", 8));
        // The emoji is one character in two UTF-16 units: nine letters and it are ten characters, and a cut keeps it whole or leaves it out.
        Assert.Equal("abcdefghi🔌", ClipboardText.Preview("abcdefghi🔌", 10));
        Assert.Equal("abcdefghi…", ClipboardText.Preview("abcdefghi🔌j", 9));
        Assert.Equal("abcdefghi🔌…", ClipboardText.Preview("abcdefghi🔌jk", 10));
    }

    [Theory]
    [InlineData(false, 300, ProgramCopyNotice.None)]
    [InlineData(false, 2000, ProgramCopyNotice.None)]
    [InlineData(false, 2001, ProgramCopyNotice.Held)]
    [InlineData(false, null, ProgramCopyNotice.Held)]
    [InlineData(true, 300, ProgramCopyNotice.Brief)]
    [InlineData(true, 60000, ProgramCopyNotice.Held)]
    [InlineData(true, null, ProgramCopyNotice.Held)]
    public void AnAgentsCopyAfterTheUsersOwnActionIsLeftToTheAgentAndOneAfterNoneIsHeld(bool shell, int? sinceMilliseconds, ProgramCopyNotice expected)
    {
        var since = sinceMilliseconds is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : (TimeSpan?)null;
        Assert.Equal(expected, ClipboardText.NoticeForProgramCopy(shell, since));
    }
}
