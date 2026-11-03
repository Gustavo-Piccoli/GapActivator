using GapActivator.Domain.Scheduling;
using GapActivator.Infrastructure.Scheduling;

namespace GapActivator.Infrastructure.Tests.Scheduling;

public sealed class IcalOccurrenceCalculatorTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
    private static readonly IcalOccurrenceCalculator Calculator = new(Zone);

    // FREQ=MINUTELY;INTERVAL=7 from 2027-03-28 00:00 CET, instants in (00:30Z, 02:30Z); derivation in the task report.
    private static readonly string[] SpringForwardMinutelyUtcTimes =
    [
        "00:31", "00:38", "00:45", "00:52", "00:59", "01:02", "01:06", "01:09", "01:13", "01:16", "01:20",
        "01:23", "01:27", "01:30", "01:34", "01:37", "01:41", "01:44", "01:48", "01:51", "01:55", "01:58",
        "02:05", "02:12", "02:19", "02:26",
    ];

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static RecurrenceRule Rule(string value) => RecurrenceRule.Parse(value);

    // DST-adjacent expansion once hung inside Ical.Net; running off the test thread lets the xUnit Timeout fail fast.
    private static Task Bounded(Action body, CancellationToken cancellationToken) => Task.Run(body, cancellationToken);

    private static List<DateTimeOffset> Walk(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset from, DateTimeOffset to)
    {
        var instants = new List<DateTimeOffset>();
        var cursor = Calculator.NextOccurrence(rule, anchor, from);
        while (cursor is { } value && value < to)
        {
            instants.Add(value);
            cursor = Calculator.NextOccurrence(rule, anchor, value);
        }

        return instants;
    }

    // Brute-force reference independent of Ical.Net: every wall-clock slot of FREQ=MINUTELY;INTERVAL=n from DTSTART,
    // mapped as documented (ambiguous -> first pass, nonexistent -> pre-gap offset), deduplicated and ordered by instant.
    private static List<DateTimeOffset> MinutelyReference(DateTime dtStart, int interval, DateTime lastWallClock)
    {
        var instants = new SortedSet<DateTimeOffset>();
        for (var wallClock = dtStart; wallClock <= lastWallClock; wallClock = wallClock.AddMinutes(interval))
        {
            var offset = Zone.IsAmbiguousTime(wallClock) ? Zone.GetAmbiguousTimeOffsets(wallClock).Max()
                : Zone.IsInvalidTime(wallClock) ? Zone.GetUtcOffset(wallClock.AddHours(-1)) // CET gaps last one hour.
                : Zone.GetUtcOffset(wallClock);
            instants.Add(new DateTimeOffset(wallClock, offset));
        }

        return [.. instants];
    }

    [Fact]
    public void FirstOccurrence_AnchorMatchesRule_ReturnsAnchor()
    {
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.FirstOccurrence(Rule("FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0"), anchor).ShouldBe(anchor);
    }

    [Fact]
    public void NextOccurrence_AfterAnchor_ReturnsFollowingDay()
    {
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(Rule("FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0"), anchor, anchor)
            .ShouldBe(At("2027-03-02T08:00:00+01:00"));
    }

    [Fact]
    public void NextOccurrence_AfterEqualsAnOccurrence_ReturnsStrictlyLater()
    {
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(Rule("FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0"), anchor, At("2027-03-02T08:00:00+01:00"))
            .ShouldBe(At("2027-03-03T08:00:00+01:00"));
    }

    [Theory(Timeout = 5000)]
    [InlineData("2027-03-28T00:00:00+01:00", "2027-03-29T00:00:00+02:00", "2027-03-28T01:30:00Z")]
    [InlineData("2027-10-31T00:00:00+02:00", "2027-11-01T00:00:00+01:00", "2027-10-31T00:30:00Z")]
    public Task NextOccurrence_DstTransitionDay_YieldsExactlyOneOccurrenceAtExpectedInstant(string dayStart, string dayEnd, string expectedInstant) =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0");
            var start = At(dayStart);
            var anchor = start.AddDays(-3);

            var instants = Walk(rule, anchor, start.AddTicks(-1), At(dayEnd));

            instants.Count.ShouldBe(1);
            instants[0].UtcDateTime.ShouldBe(At(expectedInstant).UtcDateTime);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public void NextOccurrence_FarAfterAnchorWithInterval_KeepsIntervalAlignment()
    {
        var rule = Rule("FREQ=MINUTELY;INTERVAL=7");
        var anchor = At("2027-11-01T00:00:00+01:00");
        var after = At("2027-12-31T12:00:30+01:00");

        var elapsedMinutes = (long)(after - anchor).TotalMinutes;
        var expected = anchor.AddMinutes((elapsedMinutes / 7 + 1) * 7);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = Calculator.NextOccurrence(rule, anchor, after);
        stopwatch.Stop();

        result.ShouldBe(expected);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void FirstOccurrence_NeverMatchingRule_ReturnsNull()
    {
        var rule = Rule("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30");

        Calculator.FirstOccurrence(rule, At("2027-03-01T08:00:00+01:00")).ShouldBeNull();
    }

    [Fact]
    public void NextOccurrence_NeverMatchingRule_ReturnsNull()
    {
        var rule = Rule("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30");
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(rule, anchor, anchor).ShouldBeNull();
    }

    [Fact(Timeout = 5000)]
    public Task NextOccurrence_HourlyAcrossFallBack_RunsRepeatedHourOnce() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=HOURLY;BYMINUTE=30;BYSECOND=0");
            var anchor = At("2027-10-30T22:00:00Z");

            var instants = Walk(rule, anchor, At("2027-10-30T23:00:00Z"), At("2027-10-31T05:00:00Z"));

            // 02:30 local runs on its first pass (00:30Z); the second pass (01:30Z) is skipped.
            instants.Select(i => i.UtcDateTime).ShouldBe(
            [
                At("2027-10-30T23:30:00Z").UtcDateTime,
                At("2027-10-31T00:30:00Z").UtcDateTime,
                At("2027-10-31T02:30:00Z").UtcDateTime,
                At("2027-10-31T03:30:00Z").UtcDateTime,
                At("2027-10-31T04:30:00Z").UtcDateTime,
            ]);
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task NextOccurrence_HourlyAcrossSpringForward_NeverRepeatsAnInstant() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=HOURLY;BYMINUTE=30;BYSECOND=0");
            var anchor = At("2027-03-27T22:00:00Z");

            var instants = Walk(rule, anchor, At("2027-03-27T23:00:00Z"), At("2027-03-28T04:00:00Z"));

            // Nonexistent 02:30 local uses the pre-gap offset (01:30Z); 03:30 CEST is the same instant and is not repeated.
            instants.ShouldBeUnique();
            instants.Select(i => i.UtcDateTime).ShouldBe(
            [
                At("2027-03-27T23:30:00Z").UtcDateTime,
                At("2027-03-28T00:30:00Z").UtcDateTime,
                At("2027-03-28T01:30:00Z").UtcDateTime,
                At("2027-03-28T02:30:00Z").UtcDateTime,
                At("2027-03-28T03:30:00Z").UtcDateTime,
            ]);
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task NextOccurrence_MinutelyAcrossSpringForward_IsFirstInstantAfter() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=MINUTELY;INTERVAL=7");
            var anchor = At("2027-03-27T23:00:00Z");

            var instants = Walk(rule, anchor, At("2027-03-28T00:30:00Z"), At("2027-03-28T02:30:00Z"));

            // Gap wall clocks 02:06..02:55 (pre-gap +01:00) interleave with real CEST occurrences 03:02..03:58 (+02:00).
            instants.ShouldBeUnique();
            instants.Select(i => i.UtcDateTime).ShouldBe(
                SpringForwardMinutelyUtcTimes.Select(time => At($"2027-03-28T{time}:00Z").UtcDateTime));
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task NextOccurrence_MinutelyWalkAcrossSpringForward_MatchesBruteForceMinimum() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=MINUTELY;INTERVAL=7");
            var anchor = At("2027-03-27T23:00:00Z");
            var reference = MinutelyReference(new DateTime(2027, 3, 28, 0, 0, 0), 7, new DateTime(2027, 3, 28, 10, 0, 0));

            var previous = anchor;
            while (previous < At("2027-03-28T06:00:00Z"))
            {
                var next = Calculator.NextOccurrence(rule, anchor, previous);
                next.ShouldNotBeNull();
                next.Value.UtcDateTime.ShouldBe(reference.First(r => r > previous).UtcDateTime);
                previous = next.Value;
            }
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task FirstOccurrence_AnchorInRepeatedHour_ReturnsNextWallClockOccurrence() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=HOURLY;COUNT=3");
            var anchor = At("2027-10-31T01:30:00Z");

            // The anchor (second 02:30) expands as wall clock 02:30, 03:30, 04:30; the first pass of 02:30 is before it.
            var first = Calculator.FirstOccurrence(rule, anchor);
            first.ShouldNotBeNull();
            first.Value.ShouldBeGreaterThanOrEqualTo(anchor);
            first.Value.UtcDateTime.ShouldBe(At("2027-10-31T02:30:00Z").UtcDateTime);

            var second = Calculator.NextOccurrence(rule, anchor, first.Value);
            second.ShouldNotBeNull();
            second.Value.UtcDateTime.ShouldBe(At("2027-10-31T03:30:00Z").UtcDateTime);
            Calculator.NextOccurrence(rule, anchor, second.Value).ShouldBeNull();
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task NextOccurrence_AcrossDst_OffsetFollowsZone() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0");
            var anchor = At("2027-03-01T08:00:00+01:00");

            Calculator.NextOccurrence(rule, anchor, At("2027-03-10T00:00:00+01:00"))
                .ShouldBe(At("2027-03-10T08:00:00+01:00"));
            var april = Calculator.NextOccurrence(rule, anchor, At("2027-04-10T00:00:00+02:00"));
            april.ShouldBe(At("2027-04-10T08:00:00+02:00"));
            april!.Value.Offset.ShouldBe(TimeSpan.FromHours(2));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public void NextOccurrence_WeeklyByDay_FromTuesdayReturnsWednesday()
    {
        var rule = Rule("FREQ=WEEKLY;BYDAY=MO,WE,FR;BYHOUR=9;BYMINUTE=0;BYSECOND=0");
        var anchor = At("2027-03-01T09:00:00+01:00");
        var tuesday = At("2027-03-02T10:00:00+01:00");

        Calculator.NextOccurrence(rule, anchor, tuesday).ShouldBe(At("2027-03-03T09:00:00+01:00"));
    }

    [Fact]
    public void NextOccurrence_CountExhausted_ReturnsNull()
    {
        var rule = Rule("FREQ=DAILY;COUNT=2");
        var anchor = At("2027-03-01T08:00:00+01:00");

        var first = Calculator.FirstOccurrence(rule, anchor);
        first.ShouldBe(anchor);
        var second = Calculator.NextOccurrence(rule, anchor, first!.Value);
        second.ShouldBe(anchor.AddDays(1));
        Calculator.NextOccurrence(rule, anchor, second!.Value).ShouldBeNull();
    }

    [Fact]
    public void NextOccurrence_UntilPassed_ReturnsNull()
    {
        var rule = Rule("FREQ=DAILY;UNTIL=20270303T000000Z");
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(rule, anchor, At("2027-03-02T08:00:00+01:00")).ShouldBeNull();
    }

    [Fact]
    public void NextOccurrence_UtcUntil_ComparesAsInstant()
    {
        var rule = Rule("FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0;UNTIL=20270303T073000Z");
        var anchor = At("2027-03-01T08:00:00+01:00");

        // 2027-03-03 08:00 CET is 07:00Z, before UNTIL (07:30Z), although its wall clock is later than 07:30.
        var last = Calculator.NextOccurrence(rule, anchor, At("2027-03-02T08:00:00+01:00"));
        last.ShouldBe(At("2027-03-03T08:00:00+01:00"));
        Calculator.NextOccurrence(rule, anchor, last!.Value).ShouldBeNull();
    }

    [Fact]
    public void NextOccurrence_MinutelyInterval_ReturnsNextSlot()
    {
        var rule = Rule("FREQ=MINUTELY;INTERVAL=5");
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(rule, anchor, At("2027-03-01T08:02:00+01:00"))
            .ShouldBe(At("2027-03-01T08:05:00+01:00"));
    }

    [Fact(Timeout = 5000)]
    public Task Occurrences_FourHundredDayWalk_AreStrictlyIncreasingAndNeverBeforeBounds() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0");
            var anchor = At("2027-01-01T00:00:00+01:00");

            var first = Calculator.FirstOccurrence(rule, anchor);
            first.ShouldNotBeNull();
            first.Value.ShouldBeGreaterThanOrEqualTo(anchor);

            var previous = first.Value;
            for (var day = 0; day < 400; day++)
            {
                var next = Calculator.NextOccurrence(rule, anchor, previous);
                next.ShouldNotBeNull();
                next.Value.ShouldBeGreaterThan(previous);
                previous = next.Value;
            }
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 30000)]
    public Task Occurrences_HourlyFourHundredDayWalk_AreStrictlyIncreasingWithoutLongGaps() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=HOURLY;BYMINUTE=15;BYSECOND=0");
            var anchor = At("2027-01-01T00:00:00+01:00");
            var end = anchor.AddDays(400);

            var first = Calculator.FirstOccurrence(rule, anchor);
            first.ShouldNotBeNull();
            first.Value.ShouldBe(At("2027-01-01T00:15:00+01:00"));

            var count = 1;
            var previous = first.Value;
            while (true)
            {
                var next = Calculator.NextOccurrence(rule, anchor, previous);
                next.ShouldNotBeNull();
                next.Value.ShouldBeGreaterThan(previous);
                (next.Value - previous).ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(2));
                if (next.Value >= end)
                {
                    break;
                }

                count++;
                previous = next.Value;
            }

            // One :15 instant per elapsed hour (9600), minus the skipped second pass of 02:15 on 2027-10-31.
            count.ShouldBe(9599);
        }, TestContext.Current.CancellationToken);
}
