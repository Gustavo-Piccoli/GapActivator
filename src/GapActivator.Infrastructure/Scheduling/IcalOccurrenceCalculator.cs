using GapActivator.Domain.Scheduling;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
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
        var anchorWallClock = TimeZoneInfo.ConvertTime(anchor, timeZone).DateTime;
        var calendarEvent = new CalendarEvent
        {
            Start = new CalDateTime(anchorWallClock, timeZone.Id),
            RecurrenceRule = new Ical.Net.DataTypes.RecurrenceRule(rule.Value),
        };

        // Enumeration starts at the anchor so COUNT is honored; the filter enforces the instant contract.
        foreach (var occurrence in calendarEvent.GetOccurrences(new CalDateTime(anchorWallClock, timeZone.Id)))
        {
            var instant = ToInstant(occurrence.Period.StartTime);
            if (inclusive ? instant >= bound : instant > bound)
            {
                return instant;
            }
        }

        return null;
    }

    private DateTimeOffset ToInstant(CalDateTime value)
    {
        var utc = new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc));
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }
}
