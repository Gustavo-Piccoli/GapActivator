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

    public static TimeZoneInfo Fixed(TimeSpan offset) => TimeZoneInfo.CreateCustomTimeZone("Test-Fixed", offset, "Test-Fixed", "Test-Fixed");

    private static TimeZoneInfo Create(string name, TimeSpan baseOffset, TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone(name, baseOffset, name, name, name + "-dst", [rule]);
    }
}
