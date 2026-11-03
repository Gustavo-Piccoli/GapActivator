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
    public void RecurringSchedule_Create_CalculatorReturnsNothing_IsExhaustedImmediately()
    {
        var schedule = RecurringSchedule.Create(Rule, Anchor, new FakeOccurrenceCalculator(TimeSpan.FromHours(1), count: 0));

        schedule.NextOccurrenceAt.ShouldBeNull();
        schedule.IsExhausted.ShouldBeTrue();
    }

    [Fact] // Q-13
    public void RecurringSchedule_CreateWithNotBeforeAfterAnchor_SkipsEarlierOccurrences()
    {
        var calc = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));

        var schedule = RecurringSchedule.Create(Rule, Anchor, Anchor.AddMinutes(90), calc);

        schedule.NextOccurrenceAt.ShouldBe(Anchor.AddHours(2));
    }

    [Fact] // Q-13, equality case
    public void RecurringSchedule_CreateWithNotBeforeOnAnOccurrence_KeepsThatOccurrence()
    {
        var calc = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));

        var schedule = RecurringSchedule.Create(Rule, Anchor, Anchor.AddHours(2), calc);

        schedule.NextOccurrenceAt.ShouldBe(Anchor.AddHours(2));
    }

    [Fact] // Q-13
    public void RecurringSchedule_CreateWithNotBeforeBeforeAnchor_StartsAtAnchor()
    {
        var calc = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));

        var schedule = RecurringSchedule.Create(Rule, Anchor, Anchor.AddHours(-3), calc);

        schedule.NextOccurrenceAt.ShouldBe(Anchor);
    }

    [Fact] // Q-13
    public void RecurringSchedule_CreateWithNotBeforeAfterExhaustion_IsExhausted()
    {
        var calc = new FakeOccurrenceCalculator(TimeSpan.FromHours(1), count: 2);

        var schedule = RecurringSchedule.Create(Rule, Anchor, Anchor.AddHours(5), calc);

        schedule.NextOccurrenceAt.ShouldBeNull();
        schedule.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void RecurringSchedule_Create_CalculatorResultDiffersFromAnchor_UsesTheCalculatorResult()
    {
        var first = Anchor.AddHours(3);

        var schedule = RecurringSchedule.Create(Rule, Anchor, new ScriptedCalculator(first: first, next: null));

        schedule.Anchor.ShouldBe(Anchor);
        schedule.NextOccurrenceAt.ShouldBe(first);
    }

    [Fact]
    public void RecurringSchedule_Advance_NotYetDue_ThrowsAndKeepsTheOccurrence()
    {
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        var schedule = RecurringSchedule.Create(Rule, Anchor, calculator);

        Should.Throw<InvalidOperationException>(() => schedule.Advance(Anchor.AddMinutes(-1), calculator));

        schedule.NextOccurrenceAt.ShouldBe(Anchor);
    }

    [Fact]
    public void RecurringSchedule_Advance_WhenExhausted_Throws()
    {
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1), count: 1);
        var schedule = RecurringSchedule.Create(Rule, Anchor, calculator);
        schedule.Advance(Anchor, calculator);

        Should.Throw<InvalidOperationException>(() => schedule.Advance(Anchor.AddHours(5), calculator));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void RecurringSchedule_Advance_CalculatorReturnsNonIncreasingOccurrence_Throws(int minutesFromCurrent)
    {
        var schedule = RecurringSchedule.Create(Rule, Anchor, new ScriptedCalculator(Anchor, null));
        var misbehaving = new ScriptedCalculator(Anchor, Anchor.AddMinutes(minutesFromCurrent));

        var exception = Should.Throw<InvalidOperationException>(() => schedule.Advance(Anchor, misbehaving));

        exception.Message.ShouldBe("Occurrence calculator returned a non-increasing occurrence.");
        schedule.NextOccurrenceAt.ShouldBe(Anchor);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void RecurringSchedule_Advance_CalculatorReturnsOccurrenceNotAfterNow_Throws(int minutesFromCurrent)
    {
        var schedule = RecurringSchedule.Create(Rule, Anchor, new ScriptedCalculator(Anchor, null));
        var now = Anchor.AddMinutes(30);
        var misbehaving = new ScriptedCalculator(Anchor, Anchor.AddMinutes(minutesFromCurrent));

        Should.Throw<InvalidOperationException>(() => schedule.Advance(now, misbehaving));

        schedule.NextOccurrenceAt.ShouldBe(Anchor);
    }

    [Fact]
    public void RecurringSchedule_Advance_CalculatorReturnsOccurrenceJustAfterNow_Accepts()
    {
        var schedule = RecurringSchedule.Create(Rule, Anchor, new ScriptedCalculator(Anchor, null));
        var now = Anchor.AddMinutes(30);

        schedule.Advance(now, new ScriptedCalculator(Anchor, now.AddTicks(1)));

        schedule.NextOccurrenceAt.ShouldBe(now.AddTicks(1));
    }

    [Fact]
    public void OneTimeSchedule_Advance_AlreadyFulfilled_Throws()
    {
        var schedule = OneTimeSchedule.Create(Anchor);
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        schedule.Advance(Anchor.AddMinutes(5), calculator);

        Should.Throw<InvalidOperationException>(() => schedule.Advance(Anchor.AddMinutes(10), calculator));

        schedule.FulfilledAt.ShouldBe(Anchor.AddMinutes(5));
    }

    [Fact]
    public void RecurringSchedule_Advance_CalculatorReturnsLaterOccurrence_Accepts()
    {
        var schedule = RecurringSchedule.Create(Rule, Anchor, new ScriptedCalculator(Anchor, null));

        schedule.Advance(Anchor, new ScriptedCalculator(Anchor, Anchor.AddTicks(1)));

        schedule.NextOccurrenceAt.ShouldBe(Anchor.AddTicks(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FakeOccurrenceCalculator_NonPositiveStep_Throws(int seconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeOccurrenceCalculator(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void RecurringSchedule_Create_NullRule_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            RecurringSchedule.Create(null!, Anchor, new FakeOccurrenceCalculator(TimeSpan.FromHours(1))));
    }

    /// <summary>Returns fixed answers regardless of the arguments; lets a test script a misbehaving calculator.</summary>
    private sealed class ScriptedCalculator(DateTimeOffset? first, DateTimeOffset? next) : IOccurrenceCalculator
    {
        public DateTimeOffset? FirstOccurrence(RecurrenceRule rule, DateTimeOffset anchor) => first;

        public DateTimeOffset? NextOccurrence(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset after) => next;
    }
}
