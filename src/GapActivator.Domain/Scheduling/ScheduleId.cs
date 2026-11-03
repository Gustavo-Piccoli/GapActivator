namespace GapActivator.Domain.Scheduling;

public readonly record struct ScheduleId(Guid Value)
{
    public static ScheduleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
