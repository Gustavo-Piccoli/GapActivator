namespace GapActivator.App.Services;

/// <summary>Runs the last scheduled action once nothing has been scheduled for the delay. The callback runs on a timer thread.</summary>
internal sealed class Debouncer(TimeSpan delay, TimeProvider time) : IDisposable
{
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public void Schedule(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer?.Dispose();
            _timer = time.CreateTimer(_ => _ = action(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
