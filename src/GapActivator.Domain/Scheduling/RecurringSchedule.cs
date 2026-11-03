namespace GapActivator.Domain.Scheduling;

public sealed class RecurringSchedule : Schedule
{
    private RecurringSchedule(ScheduleId id, RecurrenceRule rule, DateTimeOffset anchor)
        : base(id)
    {
        Rule = rule;
        Anchor = anchor;
    }

    public RecurrenceRule Rule { get; }

    public DateTimeOffset Anchor { get; }

    public static RecurringSchedule Create(RecurrenceRule rule, DateTimeOffset anchor, IOccurrenceCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(calculator);

        return new RecurringSchedule(ScheduleId.New(), rule, anchor)
        {
            NextOccurrenceAt = calculator.FirstOccurrence(rule, anchor),
        };
    }

    /// <summary>Occurrences missed while the service was down collapse into a single next one strictly after <paramref name="now"/>.</summary>
    internal override void Advance(DateTimeOffset now, IOccurrenceCalculator calculator)
    {
        if (NextOccurrenceAt is not { } current || now < current)
        {
            throw new InvalidOperationException("A recurring schedule can only advance once its occurrence is due.");
        }

        var next = calculator.NextOccurrence(Rule, Anchor, now);
        if (next is { } occurrence && occurrence <= current)
        {
            throw new InvalidOperationException("Occurrence calculator returned a non-increasing occurrence.");
        }

        NextOccurrenceAt = next;
    }
}
