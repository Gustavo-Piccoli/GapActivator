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

    public static RecurringSchedule Create(RecurrenceRule rule, DateTimeOffset anchor, IOccurrenceCalculator calculator) =>
        Create(rule, anchor, anchor, calculator);

    /// <summary>Occurrences before <paramref name="notBefore"/> are ignored; COUNT still counts from the anchor.</summary>
    public static RecurringSchedule Create(
        RecurrenceRule rule,
        DateTimeOffset anchor,
        DateTimeOffset notBefore,
        IOccurrenceCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(calculator);

        var first = calculator.FirstOccurrence(rule, anchor);
        return new RecurringSchedule(ScheduleId.New(), rule, anchor)
        {
            NextOccurrenceAt = first is { } f && f < notBefore
                ? calculator.NextOccurrence(rule, anchor, notBefore.AddTicks(-1))
                : first,
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
        // The port contract is "strictly after now", which also implies strictly after the current occurrence.
        if (next is { } occurrence && occurrence <= now)
        {
            throw new InvalidOperationException("Occurrence calculator returned a non-increasing occurrence.");
        }

        NextOccurrenceAt = next;
    }
}
