namespace GapActivator.App.Scheduling;

/// <summary>
/// A schedule as the editor shows it. Only the fields of <paramref name="Mode"/> mean anything.
/// <paramref name="Start"/> is a local wall-clock time (Kind Unspecified); <paramref name="Rule"/> is set for Advanced only.
/// </summary>
public sealed record FriendlySchedule(
    ScheduleMode Mode,
    int Interval,
    TimeOnly Time,
    IReadOnlyList<DayOfWeek> Days,
    int DayOfMonth,
    DateTime Start,
    string? Rule);
