using System.Globalization;
using GapActivator.App.Scheduling;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Scheduling;

public class ScheduleRulesTests
{
    private static readonly DateOnly Today = new(2030, 1, 7);
    private static readonly DateTime Start = new(2030, 1, 9, 14, 20, 45, DateTimeKind.Unspecified);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static FriendlySchedule Friendly(
        ScheduleMode mode,
        int interval = 1,
        TimeOnly? time = null,
        DayOfWeek[]? days = null,
        int dayOfMonth = 1,
        string? rule = null) =>
        new(mode, interval, time ?? new TimeOnly(8, 5), days ?? [], dayOfMonth, Start, rule);

    public static TheoryData<FriendlySchedule, ScheduleInputKind, string?, string> Rows => new()
    {
        { Friendly(ScheduleMode.EveryMinutes), ScheduleInputKind.Recurring, "FREQ=MINUTELY", "2030-01-09T14:20:00+00:00" },
        { Friendly(ScheduleMode.EveryMinutes, 15), ScheduleInputKind.Recurring, "FREQ=MINUTELY;INTERVAL=15", "2030-01-09T14:20:00+00:00" },
        { Friendly(ScheduleMode.EveryHours, 6), ScheduleInputKind.Recurring, "FREQ=HOURLY;INTERVAL=6", "2030-01-09T14:20:00+00:00" },
        { Friendly(ScheduleMode.Daily), ScheduleInputKind.Recurring, "FREQ=DAILY;BYHOUR=8;BYMINUTE=5", "2030-01-07T08:05:00+00:00" },
        { Friendly(ScheduleMode.Daily, time: new TimeOnly(0, 0)), ScheduleInputKind.Recurring, "FREQ=DAILY;BYHOUR=0;BYMINUTE=0", "2030-01-07T00:00:00+00:00" },
        { Friendly(ScheduleMode.Weekly, days: [DayOfWeek.Monday, DayOfWeek.Friday]), ScheduleInputKind.Recurring, "FREQ=WEEKLY;BYDAY=MO,FR;BYHOUR=8;BYMINUTE=5", "2030-01-07T08:05:00+00:00" },
        { Friendly(ScheduleMode.Monthly, dayOfMonth: 31, time: new TimeOnly(23, 59)), ScheduleInputKind.Recurring, "FREQ=MONTHLY;BYMONTHDAY=31;BYHOUR=23;BYMINUTE=59", "2030-01-07T23:59:00+00:00" },
        { Friendly(ScheduleMode.Once), ScheduleInputKind.OneTime, null, "2030-01-09T14:20:45+00:00" },
        { Friendly(ScheduleMode.Advanced, rule: "  FREQ=YEARLY  "), ScheduleInputKind.Recurring, "FREQ=YEARLY", "2030-01-09T14:20:45+00:00" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void ToInput_EachMode_GivesTheTableRow(FriendlySchedule schedule, ScheduleInputKind kind, string? rule, string at)
    {
        Guid id = Guid.NewGuid();

        ScheduleInputDto input = ScheduleRules.ToInput(schedule, id, Utc, Today);

        input.ShouldBe(new ScheduleInputDto(id, kind, DateTimeOffset.Parse(at, CultureInfo.InvariantCulture), rule));
    }

    [Fact]
    public void ToInput_EveryOneMinute_HasNoInterval() =>
        ScheduleRules.ToInput(Friendly(ScheduleMode.EveryMinutes, 1), null, Utc, Today).Rule.ShouldBe("FREQ=MINUTELY");

    [Fact]
    public void ToInput_WeeklyDays_AreInMondayToSundayOrder() =>
        ScheduleRules.ToInput(Friendly(ScheduleMode.Weekly, days: [DayOfWeek.Sunday, DayOfWeek.Monday]), null, Utc, Today)
            .Rule.ShouldBe("FREQ=WEEKLY;BYDAY=MO,SU;BYHOUR=8;BYMINUTE=5");

    [Fact]
    public void ToInput_WeeklyWithoutDays_Throws() =>
        Should.Throw<InvalidOperationException>(() => ScheduleRules.ToInput(Friendly(ScheduleMode.Weekly), null, Utc, Today));

    [Fact]
    public void ToInput_NoId_KeepsItNull() =>
        ScheduleRules.ToInput(Friendly(ScheduleMode.Daily), null, Utc, Today).Id.ShouldBeNull();

    // The round trip compares the wire value (preflight ruling C10), not the FriendlySchedule records.
    [Theory]
    [MemberData(nameof(Rows))]
    public void FromView_EveryGeneratedShape_RoundTripsToTheSameSchedule(FriendlySchedule schedule, ScheduleInputKind kind, string? rule, string at)
    {
        _ = (kind, rule, at);
        ScheduleInputDto input = ScheduleRules.ToInput(schedule, Guid.NewGuid(), Utc, Today);

        FriendlySchedule back = ScheduleRules.FromView(input.Kind, input.At, input.Rule, Utc);

        back.Mode.ShouldBe(schedule.Mode);
        ScheduleRules.ToInput(back, input.Id, Utc, Today).ShouldBe(input);
    }

    [Theory]
    [InlineData("FREQ=DAILY;COUNT=3;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO")]
    [InlineData("FREQ=DAILY;BYHOUR=8,20;BYMINUTE=0")]
    [InlineData("FREQ=MINUTELY;INTERVAL=2000")]
    [InlineData("FREQ=HOURLY;INTERVAL=169")]
    [InlineData("FREQ=YEARLY")]
    [InlineData("FREQ=DAILY;BYHOUR=08;BYMINUTE=0")]
    [InlineData("FREQ=DAILY;BYHOUR=24;BYMINUTE=0")]
    [InlineData("FREQ=DAILY;BYHOUR=8;BYMINUTE=60")]
    [InlineData("FREQ=WEEKLY;BYDAY=1MO;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,MO;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1;BYHOUR=8;BYMINUTE=0")]
    [InlineData("FREQ=DAILY;BYHOUR=8")]
    [InlineData("")]
    public void FromView_RuleTheFriendlyModesCannotExpress_IsAdvanced(string rule)
    {
        var at = new DateTimeOffset(2030, 1, 9, 8, 0, 0, TimeSpan.Zero);

        FriendlySchedule schedule = ScheduleRules.FromView(ScheduleInputKind.Recurring, at, rule, Utc);

        schedule.Mode.ShouldBe(ScheduleMode.Advanced);
        schedule.Rule.ShouldBe(rule);
        schedule.Start.ShouldBe(new DateTime(2030, 1, 9, 8, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void FromView_DailyAnchorAtSecond30_IsAdvanced()
    {
        var at = new DateTimeOffset(2030, 1, 9, 8, 0, 30, TimeSpan.Zero);

        ScheduleRules.FromView(ScheduleInputKind.Recurring, at, "FREQ=DAILY;BYHOUR=8;BYMINUTE=0", Utc)
            .Mode.ShouldBe(ScheduleMode.Advanced);
    }

    [Fact]
    public void FromView_LowerCaseAndReordered_StillFriendly()
    {
        var at = new DateTimeOffset(2030, 1, 9, 0, 0, 0, TimeSpan.Zero);

        FriendlySchedule schedule = ScheduleRules.FromView(ScheduleInputKind.Recurring, at, "byminute=0;freq=daily;byhour=8", Utc);

        schedule.Mode.ShouldBe(ScheduleMode.Daily);
        schedule.Time.ShouldBe(new TimeOnly(8, 0));
    }

    [Fact]
    public void FromView_Weekly_ReadsTheDaysInAnyOrder()
    {
        var at = new DateTimeOffset(2030, 1, 9, 0, 0, 0, TimeSpan.Zero);

        FriendlySchedule schedule = ScheduleRules.FromView(ScheduleInputKind.Recurring, at, "FREQ=WEEKLY;BYMINUTE=30;BYHOUR=7;BYDAY=SU,WE", Utc);

        schedule.Mode.ShouldBe(ScheduleMode.Weekly);
        schedule.Days.ShouldBe([DayOfWeek.Wednesday, DayOfWeek.Sunday], ignoreOrder: true);
        schedule.Time.ShouldBe(new TimeOnly(7, 30));
    }

    [Fact]
    public void FromView_MinutelyInterval_ReadsTheInterval()
    {
        var at = new DateTimeOffset(2030, 1, 9, 10, 15, 0, TimeSpan.Zero);

        FriendlySchedule schedule = ScheduleRules.FromView(ScheduleInputKind.Recurring, at, "FREQ=MINUTELY;INTERVAL=1440", Utc);

        schedule.Mode.ShouldBe(ScheduleMode.EveryMinutes);
        schedule.Interval.ShouldBe(1440);
    }

    [Fact]
    public void FromView_OneTime_IsOnceWithTheLocalWallClock()
    {
        var at = new DateTimeOffset(2030, 1, 9, 12, 30, 15, TimeSpan.Zero);

        FriendlySchedule schedule = ScheduleRules.FromView(ScheduleInputKind.OneTime, at, null, Zones.Fixed(TimeSpan.FromHours(-3)));

        schedule.Mode.ShouldBe(ScheduleMode.Once);
        schedule.Start.ShouldBe(new DateTime(2030, 1, 9, 9, 30, 15, DateTimeKind.Unspecified));
    }

    // Review Focus 1: the anchor's offset follows spec 6.4, so the preview and the service agree.
    [Fact]
    public void ToInput_DailyAt0230OnSpringForwardDay_UsesThePreGapOffset()
    {
        var schedule = new FriendlySchedule(ScheduleMode.Daily, 1, new TimeOnly(2, 30), [], 1, Start, null);

        ScheduleInputDto input = ScheduleRules.ToInput(schedule, null, Zones.NorthernLike, new DateOnly(2030, 3, 10));

        input.At.ShouldBe(new DateTimeOffset(2030, 3, 10, 2, 30, 0, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void ToInput_DailyAt0130OnFallBackDay_UsesTheFirstPass()
    {
        var schedule = new FriendlySchedule(ScheduleMode.Daily, 1, new TimeOnly(1, 30), [], 1, Start, null);

        ScheduleInputDto input = ScheduleRules.ToInput(schedule, null, Zones.NorthernLike, new DateOnly(2030, 11, 3));

        input.At.ShouldBe(new DateTimeOffset(2030, 11, 3, 1, 30, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void ToInput_DailyAt0230OnFallBackDay_IsAnOrdinaryTime()
    {
        var schedule = new FriendlySchedule(ScheduleMode.Daily, 1, new TimeOnly(2, 30), [], 1, Start, null);

        ScheduleInputDto input = ScheduleRules.ToInput(schedule, null, Zones.NorthernLike, new DateOnly(2030, 11, 3));

        input.At.ShouldBe(new DateTimeOffset(2030, 11, 3, 2, 30, 0, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void ToInput_DailyInANegativeDstZoneGap_UsesThePreGapOffset()
    {
        var schedule = new FriendlySchedule(ScheduleMode.Daily, 1, new TimeOnly(1, 30), [], 1, Start, null);

        ScheduleInputDto input = ScheduleRules.ToInput(schedule, null, Zones.NegativeDeltaLike, new DateOnly(2030, 3, 31));

        input.At.ShouldBe(new DateTimeOffset(2030, 3, 31, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void FromView_AnchorInTheGap_ReadsBackAFriendlyDailySchedule()
    {
        var at = new DateTimeOffset(2030, 3, 10, 2, 30, 0, TimeSpan.FromHours(-5));

        FriendlySchedule schedule = ScheduleRules.FromView(ScheduleInputKind.Recurring, at, "FREQ=DAILY;BYHOUR=2;BYMINUTE=30", Zones.NorthernLike);

        schedule.Mode.ShouldBe(ScheduleMode.Daily);
        schedule.Time.ShouldBe(new TimeOnly(2, 30));
    }
}
