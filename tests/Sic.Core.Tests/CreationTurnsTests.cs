using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class CreationTurnsTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(1), Short = TimeSpan.FromMilliseconds(50);
    private static readonly DateTimeOffset Launch = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ANewSessionWaitsForTheOneBeforeItInTheSameFolderOnly()
    {
        var turns = new CreationTurns();
        var key = CreationTurns.Key(@"C:\Users\Avery\AppData\Local\hermes", @"C:\Work\app");
        var first = await turns.TakeAsync(key, Long, CancellationToken.None);
        Assert.True(turns.Busy(key));
        // The same folder written another way waits; another folder does not.
        var second = turns.TakeAsync(CreationTurns.Key(@"C:\Users\Avery\AppData\Local\hermes\", @"c:\work\APP\"), Long, CancellationToken.None);
        var elsewhere = await turns.TakeAsync(CreationTurns.Key(@"C:\Users\Avery\AppData\Local\hermes", @"C:\Work\docs"), Long, CancellationToken.None);
        await Task.Delay(Short);
        Assert.False(second.IsCompleted);
        turns.End(first);
        var next = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(turns.Busy(key));
        turns.End(next);
        Assert.False(turns.Busy(key));
        turns.End(elsewhere);
    }

    [Fact]
    public async Task AStartGoesAheadAfterTheWaitAndEndsTheEarlierSessionsWindowAtItsLaunch()
    {
        var turns = new CreationTurns();
        var key = CreationTurns.Key(@"C:\hermes", @"C:\Work\app");
        var first = await turns.TakeAsync(key, Long, CancellationToken.None);
        turns.Launched(first, Launch);
        var second = await turns.TakeAsync(key, Short, CancellationToken.None);
        Assert.Null(first.Until);
        turns.Launched(second, Launch.AddSeconds(30));
        Assert.Equal((Launch.AddSeconds(30), (DateTimeOffset?)null), (first.Until, second.Until));
        // A later launch closes the window still open and leaves one already closed as it was.
        var third = await turns.TakeAsync(key, Short, CancellationToken.None);
        turns.Launched(third, Launch.AddSeconds(60));
        Assert.Equal((Launch.AddSeconds(30), Launch.AddSeconds(60)), (first.Until!.Value, second.Until!.Value));
        Assert.Null(third.Until);
    }

    [Fact]
    public async Task AWaitCancelledAtQuitHoldsNoTurn()
    {
        var turns = new CreationTurns();
        var key = CreationTurns.Key(@"C:\hermes", @"C:\Work\app");
        var first = await turns.TakeAsync(key, Long, CancellationToken.None);
        using var quitting = new CancellationTokenSource();
        var waiting = turns.TakeAsync(key, Long, quitting.Token);
        await quitting.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        turns.End(first);
        Assert.False(turns.Busy(key));
    }
}
