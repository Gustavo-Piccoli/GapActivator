using GapActivator.Application.Events;
using GapActivator.Application.Ports;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// Stages history entries in the scope's context; the unit of work saves them with the aggregates. Each entry gets the
/// next value of a per-scope counter as its <c>Ordinal</c>, so entries of the same instant keep their insertion order.
/// </summary>
internal sealed class EfHistoryWriter(GapActivatorDbContext db) : IHistoryWriter
{
    private int _nextOrdinal;

    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.HistoryEntries.Add(entry).Property(HistoryEntryConfiguration.OrdinalProperty).CurrentValue = _nextOrdinal++;
    }
}
