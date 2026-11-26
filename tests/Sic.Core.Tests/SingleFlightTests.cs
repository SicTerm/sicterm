using Sic.Core.Agents;
using Xunit;

namespace Sic.Core.Tests;

public sealed class SingleFlightTests
{
    [Fact]
    public async Task ACallWhileARunIsUnderWayJoinsItInsteadOfStartingAnother()
    {
        var flight = new SingleFlight<bool>();
        var release = new TaskCompletionSource<bool>();
        var starts = 0;
        Task<bool> Operation() { starts++; return release.Task; }

        Assert.Null(flight.Current);
        var first = flight.RunAsync(Operation);
        var second = flight.RunAsync(Operation);
        Assert.True(flight.Running);
        Assert.Same(release.Task, flight.Current);
        Assert.False(second.IsCompleted);
        release.SetResult(true);

        var results = await Task.WhenAll(first, second);
        Assert.Equal([true, true], results);
        Assert.Equal(1, starts);
        Assert.False(flight.Running);
        Assert.Null(flight.Current);
    }

    [Fact]
    public async Task ACallAfterARunHasEndedStartsAFreshOne()
    {
        var flight = new SingleFlight<int>();
        var starts = 0;
        async Task<int> Operation() { await Task.Yield(); return ++starts; }

        Assert.Equal(1, await flight.RunAsync(Operation));
        Assert.Equal(2, await flight.RunAsync(Operation));
    }

    [Fact]
    public async Task ARunThatCompletedSynchronouslyIsNotKeptAsTheAnswerForLaterCalls()
    {
        var flight = new SingleFlight<int>();
        var starts = 0;
        Task<int> Operation() => Task.FromResult(++starts);

        Assert.Equal(1, await flight.RunAsync(Operation));
        Assert.False(flight.Running);
        Assert.Equal(2, await flight.RunAsync(Operation));
    }

    [Fact]
    public async Task AFailedRunReachesEveryCallerAndDoesNotBlockTheNext()
    {
        var flight = new SingleFlight<bool>();
        var release = new TaskCompletionSource<bool>();
        var first = flight.RunAsync(() => release.Task);
        var joined = flight.RunAsync(() => Task.FromResult(true));
        release.SetException(new IOException("pipe closed"));

        await Assert.ThrowsAsync<IOException>(() => first);
        await Assert.ThrowsAsync<IOException>(() => joined);
        Assert.True(await flight.RunAsync(() => Task.FromResult(true)));
    }
}
