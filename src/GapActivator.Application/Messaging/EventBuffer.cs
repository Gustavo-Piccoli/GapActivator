using GapActivator.Domain.Common;

namespace GapActivator.Application.Messaging;

/// <summary>Scoped: events that no aggregate raises. They get history entries and are published like any other.</summary>
public sealed class EventBuffer
{
    private readonly List<IDomainEvent> _events = [];

    public void Record(IDomainEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _events.Add(e);
    }

    internal IReadOnlyList<IDomainEvent> Drain()
    {
        IDomainEvent[] events = [.. _events];
        _events.Clear();
        return events;
    }
}
