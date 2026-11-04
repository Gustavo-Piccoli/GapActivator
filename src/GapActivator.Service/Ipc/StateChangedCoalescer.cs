using GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Ipc;

/// <summary>
/// Limits <c>StateChanged</c> hints to one per interval. The first hint after a quiet interval goes out at once; hints that
/// follow within the interval are merged and sent once, when the interval since the last send has passed. A merged hint
/// keeps its ids only when every hint merged into it carried the same ones. <c>send</c> runs under a lock and must not
/// block or call back.
/// </summary>
internal sealed class StateChangedCoalescer(TimeSpan interval, TimeProvider time, Action<StateChangedDto> send) : IDisposable
{
    private readonly Lock _sync = new();

    private long _lastSent;
    private bool _hasSent;
    private StateChangedDto? _pending;
    private ITimer? _timer;
    private bool _disposed;

    public void Post(StateChangedDto hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending is not null)
            {
                _pending = Merge(_pending, hint);
                return;
            }

            TimeSpan sinceLast = _hasSent ? time.GetElapsedTime(_lastSent) : interval;
            if (sinceLast >= interval)
            {
                SendNow(hint);
                return;
            }

            _pending = hint;
            _timer = time.CreateTimer(Flush, null, interval - sinceLast, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private static StateChangedDto Merge(StateChangedDto pending, StateChangedDto hint) =>
        new(
            pending.ProcessId == hint.ProcessId ? pending.ProcessId : null,
            pending.RunId == hint.RunId ? pending.RunId : null);

    private void Flush(object? state)
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
            if (_disposed || _pending is not { } hint)
            {
                return;
            }

            _pending = null;
            SendNow(hint);
        }
    }

    private void SendNow(StateChangedDto hint)
    {
        _lastSent = time.GetTimestamp();
        _hasSent = true;
        send(hint);
    }
}
