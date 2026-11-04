using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Ipc;

/// <summary>The connections the server serves; it holds the connection cap and fans the pushes out to them.</summary>
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

    /// <summary>
    /// Pushes the notification to every connection past Hello. It never waits: each connection queues the push and drains
    /// it on its own.
    /// </summary>
    public void BroadcastNotification(NotificationDto notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ServerEnvelope push = ServerEnvelope.Push(
            IpcEvents.Notification,
            IpcCodec.ToElement(notification, IpcJsonContext.Default.NotificationDto));
        foreach (IpcConnection connection in Snapshot())
        {
            connection.EnqueuePush(push);
        }
    }

    /// <summary>Hands the hint to the coalescer of every connection past Hello; it never waits.</summary>
    public void PostStateChanged(StateChangedDto hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        foreach (IpcConnection connection in Snapshot())
        {
            connection.PostStateChanged(hint);
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
