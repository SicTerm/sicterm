using Sic.Core.Agents;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ProgramStatusTests
{
    [Theory]
    [InlineData("state=idle", ProgramState.Idle)]
    [InlineData("state=working", ProgramState.Working)]
    [InlineData("state=done", ProgramState.Done)]
    [InlineData("state=blocked", ProgramState.Blocked)]
    [InlineData("state=error", ProgramState.Error)]
    [InlineData("state=clear", ProgramState.Clear)]
    [InlineData(" state = idle ", ProgramState.Idle)]
    [InlineData("state=idle:state=working", ProgramState.Working)]
    [InlineData("app=claude:state=working", ProgramState.Working)]
    public void TheStateIsRead(string body, ProgramState state) => Assert.Equal(state, ProgramStatus.Parse(body)?.State);

    [Fact]
    public void TheKeysAreRead()
    {
        var status = ProgramStatus.Parse("state=blocked:kind=permission:progress=40:app=claude-code:id=build/test");
        Assert.Equal(new ProgramStatus(ProgramState.Blocked, "build/test", "permission", 40, "claude-code"), status);
    }

    [Theory]
    [InlineData("state=done:kind=permission")]
    [InlineData("state=blocked:kind=coffee")]
    public void KindBelongsToBlockedAndIsOneOfThree(string body) => Assert.Null(ProgramStatus.Parse(body)!.Kind);

    [Theory]
    [InlineData("state=working:progress=0", 0)]
    [InlineData("state=working:progress=100", 100)]
    [InlineData("state=blocked:progress=7", 7)]
    [InlineData("state=working:progress=101", null)]
    [InlineData("state=working:progress=-1", null)]
    [InlineData("state=working:progress=4.5", null)]
    [InlineData("state=done:progress=50", null)]
    public void ProgressIsAWholeShareOfWorkingOrBlocked(string body, int? progress) => Assert.Equal(progress, ProgramStatus.Parse(body)!.Progress);

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("app=claude")]
    [InlineData("state=busy")]
    [InlineData("state=wor king")]
    [InlineData("state=idle:id=a/b/c/d/e/f/g/h/i")]
    [InlineData("state=idle:id=/leading")]
    public void AReportWithoutAKnownStateOrWithABadIdIsDropped(string body) => Assert.Null(ProgramStatus.Parse(body));

    [Fact]
    public void ALimitDropsTheWholeReport()
    {
        Assert.Null(ProgramStatus.Parse("state=idle:app=" + new string('a', ProgramStatus.AppLimit + 1)));
        Assert.Null(ProgramStatus.Parse("state=idle:id=" + new string('a', ProgramStatus.IdSegmentLimit + 1)));
        Assert.Null(ProgramStatus.Parse("state=idle:msg=" + new string('A', ProgramStatus.BodyLimit)));
        Assert.NotNull(ProgramStatus.Parse("state=idle:app=" + new string('a', ProgramStatus.AppLimit)));
    }

    [Theory]
    [InlineData("state=idle:junk:foo")]
    [InlineData("state=idle:Key=1")]
    [InlineData("state=idle:averyveryverylongkeyname=1")]
    [InlineData("state=idle:msg=SGVsbG8gd29ybGQ=:title=UmV2aWV3")]
    public void UnknownAndMalformedPairsAreSkippedAndFreeTextIsNotRead(string body) => Assert.Equal(new ProgramStatus(ProgramState.Idle), ProgramStatus.Parse(body));
}
