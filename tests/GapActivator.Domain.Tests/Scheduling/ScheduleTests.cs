using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Domain.Tests.Scheduling;

public class ScheduleTests
{
    private static readonly RecurrenceRule Rule = RecurrenceRule.Parse("FREQ=HOURLY");
    private static readonly DateTimeOffset Anchor = new(2027, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScheduleId_New_ReturnsDistinctNonEmptyIds()
    {
        ScheduleId.New().Value.ShouldNotBe(Guid.Empty);
        ScheduleId.New().ShouldNotBe(ScheduleId.New());
    }

    [Fact]
    public void OneTimeSchedule_Create_SetsNextOccurrenceToAt()
    {
        var schedule = OneTimeSchedule.Create(Anchor);

        schedule.At.ShouldBe(Anchor);
        schedule.NextOccurrenceAt.ShouldBe(Anchor);
        schedule.FulfilledAt.ShouldBeNull();
        schedule.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void OneTimeSchedule_Advance_MarksFulfilledAndExhausted()
    {
        var schedule = OneTimeSchedule.Create(Anchor);
        var now = Anchor.AddMinutes(5);

        schedule.Advance(now, new FakeOccurrenceCalculator(TimeSpan.FromHours(1)));

        schedule.FulfilledAt.ShouldBe(now);
        schedule.NextOccurrenceAt.ShouldBeNull();
        schedule.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void RecurringSchedule_Create_UsesFirstOccurrence()
    {
        var schedule = RecurringSchedule.Create(Rule, Anchor, new FakeOccurrenceCalculator(TimeSpan.FromHours(1)));

        schedule.Rule.ShouldBe(Rule);
        schedule.Anchor.ShouldBe(Anchor);
        schedule.NextOccurrenceAt.ShouldBe(Anchor);
    }

    [Fact]
    public void RecurringSchedule_Advance_MissedOccurrencesCollapseIntoOne()
    {
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        var schedule = RecurringSchedule.Create(Rule, Anchor, calculator);

        schedule.Advance(Anchor.AddHours(2).AddMinutes(30), calculator);

        schedule.NextOccurrenceAt.ShouldBe(Anchor.AddHours(3));
    }

    [Fact]
    public void RecurringSchedule_Advance_NowExactlyOnOccurrence_MovesToTheNextOne()
    {
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        var schedule = RecurringSchedule.Create(Rule, Anchor, calculator);

        schedule.Advance(Anchor.AddHours(1), calculator);

        schedule.NextOccurrenceAt.ShouldBe(Anchor.AddHours(2));
    }

    [Fact]
    public void RecurringSchedule_Advance_WhenCalculatorExhausted_BecomesExhausted()
    {
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1), count: 1);
        var schedule = RecurringSchedule.Create(Rule, Anchor, calculator);

        schedule.Advance(Anchor, calculator);

        schedule.IsExhausted.ShouldBeTrue();
        schedule.NextOccurrenceAt.ShouldBeNull();
    }

    [Fact]
    public void RecurringSchedule_Create_NullRule_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            RecurringSchedule.Create(null!, Anchor, new FakeOccurrenceCalculator(TimeSpan.FromHours(1))));
    }
}
