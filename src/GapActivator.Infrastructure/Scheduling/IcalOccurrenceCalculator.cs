using GapActivator.Domain.Scheduling;
using Ical.Net;
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
/// the first pass, which lies before the anchor. A UTC UNTIL is converted to the zone's wall clock and still bounds the
/// series as an instant; a DATE-only UNTIL runs through the end of that local day. A rule with no occurrence within
/// 8 years of the search start (or of its previous occurrence) is reported as exhausted. The limit counts consecutive
/// unmatched FREQ x INTERVAL steps, 8 years rounded up to whole steps and at least one, so the effective reach can be
/// a little over 8 years (INTERVAL=3 reaches 9) and an INTERVAL of 9 years or more still searches its next step; for
/// example, FREQ=YEARLY;INTERVAL=4;BYMONTH=2;BYMONTHDAY=29 from 2027-03-01 never meets a leap year and is exhausted.
/// </remarks>
public sealed class IcalOccurrenceCalculator(TimeZoneInfo timeZone) : IOccurrenceCalculator
{
    // Wall-clock order and instant order can differ around a DST transition, so enumeration starts this far before the bound.
    private static readonly TimeSpan SearchMargin = TimeSpan.FromDays(1);

    // Safe upper bound for the spring-forward gap of any current zone.
    private static readonly TimeSpan LargestDstGap = TimeSpan.FromHours(2);

    // Longest search without a match before a rule counts as exhausted: 8 years covers leap-day rules (e.g. 2096 to 2104).
    private const int HorizonYears = 8;
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(HorizonYears * 366);

    // Later wall clocks may not map to a DateTimeOffset for every zone offset (up to +/-14 h) plus the look-ahead window.
    private static readonly DateTime LastSupportedWallClock = DateTime.MaxValue.AddDays(-1);

    public DateTimeOffset? FirstOccurrence(DomainRule rule, DateTimeOffset anchor) =>
        Find(rule, anchor, anchor, inclusive: true);

    public DateTimeOffset? NextOccurrence(DomainRule rule, DateTimeOffset anchor, DateTimeOffset after) =>
        Find(rule, anchor, after, inclusive: false);

    private DateTimeOffset? Find(DomainRule rule, DateTimeOffset anchor, DateTimeOffset bound, bool inclusive)
    {
        var pattern = new Ical.Net.DataTypes.RecurrenceRule(rule.Value);
        DateTimeOffset? untilInstant = null;
        if (pattern.Until is { IsUtc: true } until)
        {
            // Ical.Net compares a UTC UNTIL with floating occurrences as plain date-times; keep UNTIL an instant.
            untilInstant = new DateTimeOffset(DateTime.SpecifyKind(until.Value, DateTimeKind.Unspecified), TimeSpan.Zero);
            pattern.Until = Floating(WallClock(untilInstant.Value));
        }
        else if (pattern.Until is { HasTime: false } untilDate)
        {
            // Ical.Net reads a DATE-only UNTIL as midnight; it means the whole local day.
            pattern.Until = Floating(untilDate.Date.ToDateTime(new TimeOnly(23, 59, 59)));
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

        var options = new EvaluationOptions { MaxUnmatchedIncrementsLimit = UnmatchedIncrementsLimit(pattern) };

        // Gap times use the pre-gap offset, so a later wall clock can map to an earlier instant. That inversion only
        // happens near a transition, so only then are the occurrences up to LargestDstGap later compared as well.
        DateTimeOffset? earliest = null;
        var windowEnd = DateTime.MaxValue;
        try
        {
            foreach (var occurrence in calendarEvent.GetOccurrences(Floating(start), options))
            {
                var wallClock = occurrence.Period.StartTime.Value;
                if (wallClock > windowEnd || wallClock > LastSupportedWallClock)
                {
                    break;
                }

                var instant = ToInstant(wallClock);
                if ((inclusive ? instant < bound : instant <= bound) || instant > untilInstant)
                {
                    continue;
                }

                if (earliest is null)
                {
                    if (!NearTransition(wallClock))
                    {
                        return instant;
                    }

                    earliest = instant;
                    windowEnd = wallClock + LargestDstGap;
                }
                else if (instant < earliest)
                {
                    earliest = instant;
                }
            }
        }
        catch (EvaluationLimitExceededException)
        {
            // No match within the search horizon: the series is exhausted after whatever was already found.
        }
        catch (EvaluationOutOfRangeException)
        {
            // Ical.Net reached its maximum date-time: the series is exhausted after whatever was already found.
        }

        return earliest;
    }

    private bool NearTransition(DateTime wallClock) =>
        timeZone.IsInvalidTime(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified))
        || ToInstant(wallClock).Offset != ToInstant(wallClock + LargestDstGap).Offset;

    // Ical.Net counts consecutive increments of FREQ x INTERVAL without a match; the limit spans the search horizon.
    private static int UnmatchedIncrementsLimit(Ical.Net.DataTypes.RecurrenceRule pattern)
    {
        var units = pattern.Frequency switch
        {
            FrequencyType.Secondly => Horizon.TotalSeconds,
            FrequencyType.Minutely => Horizon.TotalMinutes,
            FrequencyType.Hourly => Horizon.TotalHours,
            FrequencyType.Daily => Horizon.TotalDays,
            FrequencyType.Weekly => Horizon.TotalDays / 7,
            FrequencyType.Monthly => HorizonYears * 12,
            _ => HorizonYears,
        };

        return Math.Max(1, (int)Math.Ceiling(units / Math.Max(1, pattern.Interval)));
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
