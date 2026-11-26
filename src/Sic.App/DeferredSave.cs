using Microsoft.UI.Dispatching;
using Sic.Core.State;

namespace Sic.App;

/// <summary>
/// A state file written at most once per quiet period, and off the UI thread. A request starts the quiet period over; when it
/// runs out, the state is serialized on the UI thread, where it is changed, and written on a thread-pool task. Writes queue one
/// behind the other, so an older one never lands after a newer. <see cref="Flush"/> writes a pending request at once and waits
/// for every write to finish: for quitting, and for a check that reads the file back.
/// </summary>
internal sealed class DeferredSave<T> where T : class
{
    private readonly JsonStateStore<T> _store;
    private readonly Func<T> _state;
    private readonly Action<Exception> _failed;
    private readonly DispatcherQueueTimer _timer;
    private Task _writes = Task.CompletedTask;

    public DeferredSave(DispatcherQueue queue, JsonStateStore<T> store, Func<T> state, TimeSpan quiet, Action<Exception> failed)
    {
        _store = store;
        _state = state;
        _failed = failed;
        _timer = queue.CreateTimer();
        _timer.Interval = quiet;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => WriteNow();
    }

    /// <summary>Whether a request is waiting out its quiet period or a write is still under way.</summary>
    public bool Pending => _timer.IsRunning || !_writes.IsCompleted;

    /// <summary>Saves once the state has been quiet for the period; a request during the period starts it over.</summary>
    public void Request()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Writes a pending request now and waits for the writes to land. A write that fails is reported, not thrown.</summary>
    public void Flush()
    {
        if (_timer.IsRunning)
        {
            _timer.Stop();
            WriteNow();
        }
        try { _writes.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { /* Reported by the write itself. */ }
    }

    private void WriteNow()
    {
        byte[] serialized;
        try { serialized = _store.Serialize(_state()); }
        catch (Exception exception) { _failed(exception); return; }
        _writes = _writes.ContinueWith(_ =>
        {
            try { _store.Write(serialized); }
            catch (Exception exception) { _failed(exception); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
