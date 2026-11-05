namespace GapActivator.App.Scheduling;

/// <summary>The friendly ways to describe when a process runs; <see cref="Advanced"/> is a raw RRULE.</summary>
public enum ScheduleMode
{
    EveryMinutes,
    EveryHours,
    Daily,
    Weekly,
    Monthly,
    Once,
    Advanced,
}
