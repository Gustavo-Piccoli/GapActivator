using GapActivator.Domain.Common;

namespace GapActivator.Domain.Scheduling;

/// <summary>Decides when a process is next due.</summary>
public abstract class Schedule(ScheduleId id) : Entity<ScheduleId>(id)
{
    public DateTimeOffset? NextOccurrenceAt { get; protected set; }

    public bool IsExhausted => NextOccurrenceAt is null;

    /// <summary>Moves past the occurrence that was just dispatched at <paramref name="now"/>.</summary>
    internal abstract void Advance(DateTimeOffset now, IOccurrenceCalculator calculator);
}
