using GapActivator.Domain.Scheduling;
using GapActivator.Infrastructure.Scheduling;

namespace GapActivator.Infrastructure.Tests.Scheduling;

/// <summary>
/// Sub-daily rules expanded over the whole search horizon are CPU-bound; they run alone so that a loaded machine
/// cannot starve them into their timeout.
/// </summary>
[Collection(Name)]
public sealed class IcalOccurrenceCalculatorHeavyTests
{
    public const string Name = "IcalHeavyExpansion";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
    private static readonly IcalOccurrenceCalculator Calculator = new(Zone);

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static RecurrenceRule Rule(string value) => RecurrenceRule.Parse(value);

    // Expansion once hung inside Ical.Net; running off the test thread lets the xUnit Timeout fail fast.
    private static Task Bounded(Action body, CancellationToken cancellationToken) => Task.Run(body, cancellationToken);

    // Without a search limit Ical.Net steps every minute or hour to year 9999 (probe: HOURLY 20 s, MINUTELY > 40 s).
    [Theory(Timeout = 60000)]
    [InlineData("FREQ=MINUTELY;BYMONTH=2;BYMONTHDAY=29;BYDAY=TH")]
    [InlineData("FREQ=HOURLY;BYMONTH=2;BYMONTHDAY=29;BYDAY=TH")]
    public Task Occurrences_NoMatchWithinHorizonSubDailyRule_ReturnNullForBothMethods(string value) =>
        Bounded(() =>
        {
            var rule = Rule(value);
            var anchor = At("2027-03-01T08:00:00+01:00");

            Calculator.FirstOccurrence(rule, anchor).ShouldBeNull();
            Calculator.NextOccurrence(rule, anchor, anchor).ShouldBeNull();
        }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 60000)]
    public Task FirstOccurrence_MinutelyLeapDayRule_ReturnsNextLeapDayWithinHorizon() =>
        Bounded(() =>
        {
            var rule = Rule("FREQ=MINUTELY;BYMONTH=2;BYMONTHDAY=29;BYHOUR=0;BYMINUTE=0");

            Calculator.FirstOccurrence(rule, At("2027-03-01T00:00:00+01:00")).ShouldBe(At("2028-02-29T00:00:00+01:00"));
        }, TestContext.Current.CancellationToken);
}

[CollectionDefinition(IcalOccurrenceCalculatorHeavyTests.Name, DisableParallelization = true)]
public sealed class IcalHeavyExpansionSerialization;
