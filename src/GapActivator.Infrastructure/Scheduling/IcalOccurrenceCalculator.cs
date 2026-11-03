using GapActivator.Domain.Scheduling;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using DomainRule = GapActivator.Domain.Scheduling.RecurrenceRule;

namespace GapActivator.Infrastructure.Scheduling;

/// <summary>Expands recurrence rules with Ical.Net, interpreting wall-clock times in a fixed time zone.</summary>
public sealed class IcalOccurrenceCalculator(TimeZoneInfo timeZone) : IOccurrenceCalculator
{
    public DateTimeOffset? FirstOccurrence(DomainRule rule, DateTimeOffset anchor) =>
        Find(rule, anchor, anchor, inclusive: true);

    public DateTimeOffset? NextOccurrence(DomainRule rule, DateTimeOffset anchor, DateTimeOffset after) =>
        Find(rule, anchor, after, inclusive: false);

    private DateTimeOffset? Find(DomainRule rule, DateTimeOffset anchor, DateTimeOffset bound, bool inclusive)
    {
        var elapsed = IsElapsedAnchor(rule, anchor);
        var anchorLocal = ToCalDateTime(anchor, elapsed);
        var calendarEvent = new CalendarEvent
        {
            Start = anchorLocal,
            RecurrenceRule = new Ical.Net.DataTypes.RecurrenceRule(rule.Value),
        };

        // Without COUNT, enumeration can start near the bound (DTSTART keeps INTERVAL/BY* alignment);
        // with COUNT it must start at the anchor so the series is counted from there.
        var start = anchorLocal;
        if (!HasCount(rule) && bound > anchor)
        {
            start = ToCalDateTime(bound, elapsed);
        }

        try
        {
            foreach (var occurrence in calendarEvent.GetOccurrences(start))
            {
                var instant = ToInstant(occurrence.Period.StartTime);
                if (inclusive ? instant >= bound : instant > bound)
                {
                    return instant;
                }
            }
        }
        catch (EvaluationOutOfRangeException)
        {
            // A rule that never matches (e.g. February 30th) makes Ical.Net enumerate to year 9999 and throw: series exhausted.
        }

        return null;
    }

    private CalDateTime ToCalDateTime(DateTimeOffset value, bool utc) =>
        utc
            ? new CalDateTime(value.UtcDateTime)
            : new CalDateTime(TimeZoneInfo.ConvertTime(value, timeZone).DateTime, timeZone.Id);

    // A wall-clock DTSTART cannot express the second pass of an ambiguous local time (Ical.Net resolves it to the
    // first pass), so sub-daily rules anchored there run on UTC, where the series is elapsed-time arithmetic.
    private bool IsElapsedAnchor(DomainRule rule, DateTimeOffset anchor)
    {
        var wallClock = TimeZoneInfo.ConvertTime(anchor, timeZone).DateTime;
        return IsSubDaily(rule)
            && timeZone.IsAmbiguousTime(wallClock)
            && anchor.Offset < timeZone.GetAmbiguousTimeOffsets(wallClock).Max();
    }

    private static bool IsSubDaily(DomainRule rule) =>
        rule.Value.Contains("FREQ=HOURLY", StringComparison.OrdinalIgnoreCase)
        || rule.Value.Contains("FREQ=MINUTELY", StringComparison.OrdinalIgnoreCase)
        || rule.Value.Contains("FREQ=SECONDLY", StringComparison.OrdinalIgnoreCase);

    private static bool HasCount(DomainRule rule) =>
        rule.Value.Contains("COUNT=", StringComparison.OrdinalIgnoreCase);

    private DateTimeOffset ToInstant(CalDateTime value)
    {
        var utc = new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc));
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }
}
