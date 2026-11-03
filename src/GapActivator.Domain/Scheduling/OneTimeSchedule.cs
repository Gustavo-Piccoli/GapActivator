namespace GapActivator.Domain.Scheduling;

public sealed class OneTimeSchedule : Schedule
{
    private OneTimeSchedule(ScheduleId id, DateTimeOffset at)
        : base(id)
    {
        At = at;
        NextOccurrenceAt = at;
    }

    public DateTimeOffset At { get; }

    public DateTimeOffset? FulfilledAt { get; private set; }

    public static OneTimeSchedule Create(DateTimeOffset at) => new(ScheduleId.New(), at);

    internal override void Advance(DateTimeOffset now, IOccurrenceCalculator calculator)
    {
        if (FulfilledAt is not null)
        {
            throw new InvalidOperationException("A one-time schedule has already been fulfilled.");
        }

        FulfilledAt = now;
        NextOccurrenceAt = null;
    }
}
