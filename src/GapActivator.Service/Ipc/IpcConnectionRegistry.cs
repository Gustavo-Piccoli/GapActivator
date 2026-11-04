namespace GapActivator.Service.Ipc;

/// <summary>The connections the server serves; it holds the connection cap.</summary>
internal sealed class IpcConnectionRegistry
{
    private readonly Lock _sync = new();
    private readonly List<IpcConnection> _connections = [];

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _connections.Count;
            }
        }
    }

    /// <returns>False, and nothing is added, when <paramref name="max"/> connections are already registered.</returns>
    public bool TryAdd(IpcConnection connection, int max)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_sync)
        {
            if (_connections.Count >= max)
            {
                return false;
            }

            _connections.Add(connection);
            return true;
        }
    }

    public void Remove(IpcConnection connection)
    {
        lock (_sync)
        {
            _connections.Remove(connection);
        }
    }

    public IReadOnlyList<IpcConnection> Snapshot()
    {
        lock (_sync)
        {
            return [.. _connections];
        }
    }
}
