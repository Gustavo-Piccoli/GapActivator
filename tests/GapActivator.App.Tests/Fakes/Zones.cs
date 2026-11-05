namespace GapActivator.App.Tests.Fakes;

/// <summary>Explicit zones with daylight saving, so no test depends on the machine's zone.</summary>
internal static class Zones
{
    /// <summary>Base -05:00, +1 h from the second Sunday of March 02:00 (spring forward) to the first Sunday of November 02:00 (fall back).</summary>
    public static TimeZoneInfo NorthernLike { get; } = Create(
        "Test-North",
        TimeSpan.FromHours(-5),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));

    /// <summary>Base -03:00, +1 h from the first Sunday of November 00:00 until the first Sunday of March 00:00 (a southern-hemisphere shape).</summary>
    public static TimeZoneInfo SouthernLike { get; } = Create(
        "Test-South",
        TimeSpan.FromHours(-3),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1, DayOfWeek.Sunday),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 1, DayOfWeek.Sunday));

    /// <summary>Base +01:00 with a daylight delta of -1 h (the shape of Dublin's rules): +00:00 from the last Sunday of October 02:00 to the last Sunday of March 01:00.</summary>
    public static TimeZoneInfo NegativeDeltaLike { get; } = Create(
        "Test-Negative",
        TimeSpan.FromHours(1),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday),
        TimeSpan.FromHours(-1));

    /// <summary>Base +02:00 with a daylight delta of -2 h: on the last Sunday of March 01:00 jumps to 03:00, a gap of two hours.</summary>
    public static TimeZoneInfo TwoHourGapLike { get; } = Create(
        "Test-TwoHourGap",
        TimeSpan.FromHours(2),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday),
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday),
        TimeSpan.FromHours(-2));

    public static TimeZoneInfo Fixed(TimeSpan offset) => TimeZoneInfo.CreateCustomTimeZone("Test-Fixed", offset, "Test-Fixed", "Test-Fixed");

    private static TimeZoneInfo Create(string name, TimeSpan baseOffset, TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end, TimeSpan? delta = null)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, delta ?? TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone(name, baseOffset, name, name, name + "-dst", [rule]);
    }
}
