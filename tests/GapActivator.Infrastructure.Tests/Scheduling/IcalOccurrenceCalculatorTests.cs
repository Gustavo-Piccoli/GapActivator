using GapActivator.Domain.Scheduling;
using GapActivator.Infrastructure.Scheduling;

namespace GapActivator.Infrastructure.Tests.Scheduling;

public sealed class IcalOccurrenceCalculatorTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
    private static readonly IcalOccurrenceCalculator Calculator = new(Zone);

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static RecurrenceRule Rule(string value) => RecurrenceRule.Parse(value);

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

    [Theory]
    [InlineData("2027-03-28T00:00:00+01:00", "2027-03-29T00:00:00+02:00")]
    [InlineData("2027-10-31T00:00:00+02:00", "2027-11-01T00:00:00+01:00")]
    public void NextOccurrence_DstTransitionDay_YieldsExactlyOneOccurrence(string dayStart, string dayEnd)
    {
        var rule = Rule("FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0");
        var start = At(dayStart);
        var end = At(dayEnd);
        var anchor = start.AddDays(-3);

        var count = 0;
        var cursor = Calculator.NextOccurrence(rule, anchor, start.AddTicks(-1));
        while (cursor is { } value && value < end)
        {
            count++;
            cursor = Calculator.NextOccurrence(rule, anchor, value);
        }

        count.ShouldBe(1);
    }

    [Fact]
    public void NextOccurrence_AcrossDst_OffsetFollowsZone()
    {
        var rule = Rule("FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0");
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(rule, anchor, At("2027-03-10T00:00:00+01:00"))
            .ShouldBe(At("2027-03-10T08:00:00+01:00"));
        var april = Calculator.NextOccurrence(rule, anchor, At("2027-04-10T00:00:00+02:00"));
        april.ShouldBe(At("2027-04-10T08:00:00+02:00"));
        april!.Value.Offset.ShouldBe(TimeSpan.FromHours(2));
    }

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
    public void NextOccurrence_MinutelyInterval_ReturnsNextSlot()
    {
        var rule = Rule("FREQ=MINUTELY;INTERVAL=5");
        var anchor = At("2027-03-01T08:00:00+01:00");

        Calculator.NextOccurrence(rule, anchor, At("2027-03-01T08:02:00+01:00"))
            .ShouldBe(At("2027-03-01T08:05:00+01:00"));
    }

    [Fact]
    public void Occurrences_FourHundredDayWalk_AreStrictlyIncreasingAndNeverBeforeBounds()
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
    }
}
