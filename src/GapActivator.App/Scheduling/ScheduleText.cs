using System.Globalization;
using GapActivator.App.Localization;
using GapActivator.App.Services;

namespace GapActivator.App.Scheduling;

/// <summary>One-line, localized description of a schedule, for lists.</summary>
internal static class ScheduleText
{
    private static readonly DayOfWeek[] MondayFirst =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static string Summary(FriendlySchedule schedule, Localizer l, TimeDisplay times, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(zone);

        return schedule.Mode switch
        {
            ScheduleMode.EveryMinutes => l.EveryMinutesSummary(schedule.Interval),
            ScheduleMode.EveryHours => l.EveryHoursSummary(schedule.Interval),
            ScheduleMode.Daily => l.DailySummary(times.Time(schedule.Time)),
            ScheduleMode.Weekly => l.WeeklySummary(
                string.Join(", ", MondayFirst.Where(schedule.Days.Contains).Select(l.DayShort)),
                times.Time(schedule.Time)),
            ScheduleMode.Monthly => l.MonthlySummary(schedule.DayOfMonth.ToString(CultureInfo.InvariantCulture), times.Time(schedule.Time)),
            ScheduleMode.Once => l.OnceSummary(times.DateTime(LocalTime.ToOffset(schedule.Start, zone))),
            ScheduleMode.Advanced => schedule.Rule ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(schedule)),
        };
    }
}
