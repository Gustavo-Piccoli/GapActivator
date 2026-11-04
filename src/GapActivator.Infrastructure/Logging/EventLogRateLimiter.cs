namespace GapActivator.Infrastructure.Logging;

/// <summary>Admits each (category, message) pair to the Event Log once per <see cref="Window"/>, bounded to <see cref="Capacity"/> keys.</summary>
internal sealed class EventLogRateLimiter(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public const int Capacity = 1000;

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Category, string Message), long> _stamps = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _stamps.Count;
            }
        }
    }

    /// <summary>True once per (category, message) per <see cref="Window"/>. Monotonic time, so a clock step never releases a key early.</summary>
    public bool TryAcquire(string category, string message)
    {
        var key = (category, message);
        lock (_gate)
        {
            long now = time.GetTimestamp();
            if (_stamps.TryGetValue(key, out long stamp))
            {
                if (time.GetElapsedTime(stamp, now) < Window)
                {
                    return false;
                }

                _stamps[key] = now;
                return true;
            }

            if (_stamps.Count >= Capacity)
            {
                MakeRoom(now);
            }

            _stamps[key] = now;
            return true;
        }
    }

    private void MakeRoom(long now)
    {
        List<(string Category, string Message)> expired = [];
        foreach (KeyValuePair<(string Category, string Message), long> entry in _stamps)
        {
            if (time.GetElapsedTime(entry.Value, now) >= Window)
            {
                expired.Add(entry.Key);
            }
        }

        foreach ((string Category, string Message) key in expired)
        {
            _stamps.Remove(key);
        }

        if (_stamps.Count < Capacity)
        {
            return;
        }

        var oldest = default((string Category, string Message));
        long oldestStamp = long.MaxValue;
        foreach (KeyValuePair<(string Category, string Message), long> entry in _stamps)
        {
            if (entry.Value < oldestStamp)
            {
                oldestStamp = entry.Value;
                oldest = entry.Key;
            }
        }

        _stamps.Remove(oldest);
    }
}
