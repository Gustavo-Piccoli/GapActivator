using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Scheduling;

public class ScheduleTextTests
{
    private static readonly TimeZoneInfo Zone = Zones.Fixed(TimeSpan.FromHours(-3));
    private static readonly DateTime Start = new(2030, 2, 1, 9, 30, 0, DateTimeKind.Unspecified);

    private static FriendlySchedule Friendly(ScheduleMode mode, int interval = 1, DayOfWeek[]? days = null, string? rule = null) =>
        new(mode, interval, new TimeOnly(8, 5), days ?? [], 15, Start, rule);

    private static string Summary(FriendlySchedule schedule, AppLanguage language = AppLanguage.English)
    {
        var l = new Localizer(language);
        return ScheduleText.Summary(schedule, l, new TimeDisplay(l, Zone), Zone);
    }

    [Theory]
    [InlineData(ScheduleMode.EveryMinutes, 1, "Every minute")]
    [InlineData(ScheduleMode.EveryMinutes, 15, "Every 15 minutes")]
    [InlineData(ScheduleMode.EveryHours, 1, "Every hour")]
    [InlineData(ScheduleMode.EveryHours, 6, "Every 6 hours")]
    [InlineData(ScheduleMode.Daily, 1, "Daily at 8:05 AM")]
    [InlineData(ScheduleMode.Monthly, 1, "Monthly on day 15 at 8:05 AM")]
    [InlineData(ScheduleMode.Once, 1, "Once on 2/1/2030 9:30 AM")]
    public void Summary_EachMode_ReadsAsASentence(ScheduleMode mode, int interval, string expected) =>
        Summary(Friendly(mode, interval)).ShouldBe(expected);

    [Fact]
    public void Summary_Weekly_ListsTheDaysFromMonday() =>
        Summary(Friendly(ScheduleMode.Weekly, days: [DayOfWeek.Sunday, DayOfWeek.Monday])).ShouldBe("Weekly on Mon, Sun at 8:05 AM");

    [Fact]
    public void Summary_Advanced_IsTheRule() =>
        Summary(Friendly(ScheduleMode.Advanced, rule: "FREQ=YEARLY")).ShouldBe("FREQ=YEARLY");

    [Fact]
    public void Summary_Portuguese_UsesTheFixedWording() =>
        Summary(Friendly(ScheduleMode.Daily), AppLanguage.Portuguese).ShouldBe("Diariamente às 08:05");
}
