using GapActivator.Domain.Scheduling;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using DomainRule = GapActivator.Domain.Scheduling.RecurrenceRule;

namespace GapActivator.Infrastructure.Scheduling;

/// <summary>Expands recurrence rules with Ical.Net, interpreting wall-clock times in a fixed time zone.</summary>
/// <remarks>
/// Rules expand on the wall clock of the configured zone: Ical.Net only sees floating date-times, and this class maps
/// each wall-clock occurrence to an instant. INTERVAL therefore counts wall-clock time, so the cadence shifts by the DST
/// change across a transition. A nonexistent local time (spring-forward gap) uses the pre-gap offset; mapped gap times
/// are ordered by instant together with the real post-gap occurrences, and two that land on the same instant run once.
/// An ambiguous local time (fall-back overlap) resolves to its first pass, so sub-daily rules run the repeated hour once,
/// and an anchor inside the repeated hour is treated as its wall-clock value; with COUNT, such an anchor spends a slot on
/// the first pass, which lies before the anchor. A UTC UNTIL is converted to the zone's wall clock, so it still bounds
/// the series as an instant.
/// </remarks>
public sealed class IcalOccurrenceCalculator(TimeZoneInfo timeZone) : IOccurrenceCalculator
{
    // Wall-clock order and instant order can differ around a DST transition, so enumeration starts this far before the bound.
    private static readonly TimeSpan SearchMargin = TimeSpan.FromDays(1);

    // Safe upper bound for any zone's spring-forward gap.
    private static readonly TimeSpan LargestDstGap = TimeSpan.FromHours(2);

    public DateTimeOffset? FirstOccurrence(DomainRule rule, DateTimeOffset anchor) =>
        Find(rule, anchor, anchor, inclusive: true);

    public DateTimeOffset? NextOccurrence(DomainRule rule, DateTimeOffset anchor, DateTimeOffset after) =>
        Find(rule, anchor, after, inclusive: false);

    private DateTimeOffset? Find(DomainRule rule, DateTimeOffset anchor, DateTimeOffset bound, bool inclusive)
    {
        var pattern = new Ical.Net.DataTypes.RecurrenceRule(rule.Value);
        if (pattern.Until is { IsUtc: true } until)
        {
            // Ical.Net compares a UTC UNTIL with floating occurrences as plain date-times; keep UNTIL an instant.
            var untilUtc = new DateTimeOffset(DateTime.SpecifyKind(until.Value, DateTimeKind.Unspecified), TimeSpan.Zero);
            pattern.Until = Floating(WallClock(untilUtc));
        }

        var anchorWallClock = WallClock(anchor);
        var calendarEvent = new CalendarEvent
        {
            Start = Floating(anchorWallClock),
            RecurrenceRule = pattern,
        };

        // Without COUNT, enumeration can start near the bound (DTSTART keeps INTERVAL/BY* alignment);
        // with COUNT it must start at the anchor so the series is counted from there.
        var start = anchorWallClock;
        if (pattern.Count is null && bound > anchor)
        {
            var nearBound = WallClock(bound) - SearchMargin;
            if (nearBound > start)
            {
                start = nearBound;
            }
        }

        // Gap times use the pre-gap offset, so a later wall clock can map to an earlier instant. Instant order can only be
        // inverted within the zone's DST gap, so after the first match the occurrences up to that window are compared too.
        DateTimeOffset? earliest = null;
        var windowEnd = DateTime.MaxValue;
        try
        {
            foreach (var occurrence in calendarEvent.GetOccurrences(Floating(start)))
            {
                var wallClock = occurrence.Period.StartTime.Value;
                if (wallClock > windowEnd)
                {
                    break;
                }

                var instant = ToInstant(wallClock);
                if (inclusive ? instant < bound : instant <= bound)
                {
                    continue;
                }

                if (earliest is null)
                {
                    windowEnd = wallClock + LargestDstGap;
                }

                if (earliest is null || instant < earliest)
                {
                    earliest = instant;
                }
            }
        }
        catch (EvaluationOutOfRangeException)
        {
            // A rule that never matches again (e.g. February 30th) makes Ical.Net enumerate to year 9999 and throw:
            // the series is exhausted after whatever was already found.
        }

        return earliest;
    }

    private DateTime WallClock(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, timeZone).DateTime;

    private static CalDateTime Floating(DateTime wallClock) =>
        new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), hasTime: true);

    private DateTimeOffset ToInstant(DateTime wallClock)
    {
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (timeZone.IsAmbiguousTime(local))
        {
            offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
        }
        else if (timeZone.IsInvalidTime(local))
        {
            offset = PreGapOffset(local);
        }
        else
        {
            offset = timeZone.GetUtcOffset(local);
        }

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(local, offset), timeZone);
    }

    // The last valid local time before a spring-forward gap carries the offset in effect before the gap.
    private TimeSpan PreGapOffset(DateTime nonexistent)
    {
        var beforeGap = nonexistent;
        while (timeZone.IsInvalidTime(beforeGap))
        {
            beforeGap = beforeGap.AddMinutes(-1);
        }

        return timeZone.GetUtcOffset(beforeGap);
    }
}
