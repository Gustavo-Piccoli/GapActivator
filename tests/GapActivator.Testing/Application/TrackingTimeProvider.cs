using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Testing;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that counts the timers created from it. Advancing the clock before a background
/// task has created its timer would make that task wait forever, so a test first awaits
/// <see cref="WaitForTimersAsync"/> and only then calls Advance. The count is cumulative: a disposed or fired timer
/// still counts. Every Task.Delay with a finite positive delay on this provider creates exactly one timer.
/// </summary>
public sealed class TrackingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly Lock _sync = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private int _timersCreated;

    /// <summary>How many timers were created so far.</summary>
    public int TimersCreated
    {
        get
        {
            lock (_sync)
            {
                return _timersCreated;
            }
        }
    }

    /// <summary>Completes once at least <paramref name="count"/> timers exist, so advancing the clock will fire them.</summary>
    public Task WaitForTimersAsync(int count)
    {
        lock (_sync)
        {
            if (_timersCreated >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, signal));
            return signal.Task;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);
        lock (_sync)
        {
            _timersCreated++;
            foreach ((int Count, TaskCompletionSource Signal) waiter in _waiters.Where(w => w.Count <= _timersCreated))
            {
                waiter.Signal.TrySetResult();
            }

            _waiters.RemoveAll(w => w.Count <= _timersCreated);
        }

        return timer;
    }
}
