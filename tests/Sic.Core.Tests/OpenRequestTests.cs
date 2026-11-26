using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class OpenRequestTests
{
    [Fact]
    public void AFolderAndAnAgentSurviveTheMessage()
    {
        var request = new OpenRequest(@"C:\Work\Blue Maple", "claude");
        Assert.Equal(request, OpenRequest.Decode(request.Encode()));
    }

    [Fact]
    public void AFolderAloneComesBackWithoutAnAgent()
    {
        var request = new OpenRequest(@"D:\");
        Assert.Equal(@"D:\" + "\n", request.Encode());
        Assert.Equal(request, OpenRequest.Decode(request.Encode()));
        // The terminator a Win32 buffer carries, and a stray return, are not part of the agent.
        Assert.Equal(request, OpenRequest.Decode(@"D:\" + "\n\0"));
        Assert.Equal(request, OpenRequest.Decode(@"D:\" + "\r\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\nclaude")]
    [InlineData("C:\\Work\0\nclaude")]
    public void TextThatIsNotARequestDecodesToNothing(string? text) => Assert.Null(OpenRequest.Decode(text));

    [Fact]
    public void AProblemNamesARelativePathOrAMissingFolder()
    {
        Assert.Contains("not an absolute", OpenRequest.Problem(@"Work\Blue", _ => true));
        Assert.Contains("does not exist", OpenRequest.Problem(@"C:\Work\Blue", _ => false));
        Assert.Null(OpenRequest.Problem(@"C:\Work\Blue", _ => true));
    }
}
