using System.Runtime.InteropServices;
using Sic.Core.Shell;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ClipboardRetryTests
{
    private static Exception Busy() => new COMException("OpenClipboard Failed", ClipboardRetry.CantOpen);

    [Fact]
    public void AWriteThatLandsAtOnceIsTriedOnceAndWaitsForNothing()
    {
        var waits = new List<TimeSpan>();
        Assert.Equal(1, ClipboardRetry.Run(() => { }, waits.Add));
        Assert.Empty(waits);
    }

    [Fact]
    public void AClipboardHeldForAMomentIsWaitedOutWithLongerWaitsEachTime()
    {
        var tries = 0;
        var waits = new List<TimeSpan>();
        var attempt = ClipboardRetry.Run(() => { if (++tries < 3) throw Busy(); }, waits.Add);
        Assert.Equal(3, attempt);
        Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)], waits);
    }

    [Fact]
    public void AClipboardHeldPastTheLastWaitFailsAfterOneTryMoreThanThereAreWaits()
    {
        var tries = 0;
        var waits = new List<TimeSpan>();
        var failure = Assert.Throws<COMException>(() => ClipboardRetry.Run(() => { tries++; throw Busy(); }, waits.Add));
        Assert.Equal(ClipboardRetry.CantOpen, failure.HResult);
        Assert.Equal(ClipboardRetry.Waits.Count + 1, tries);
        Assert.Equal(ClipboardRetry.Waits, waits);
        Assert.Equal(150, waits.Sum(wait => wait.TotalMilliseconds));
    }

    [Fact]
    public void AnyOtherFailureIsThrownAtOnce()
    {
        var tries = 0;
        Assert.Throws<InvalidOperationException>(() => ClipboardRetry.Run(() => { tries++; throw new InvalidOperationException("no"); }, _ => { }));
        Assert.Equal(1, tries);
    }
}
