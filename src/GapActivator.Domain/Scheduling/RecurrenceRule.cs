using System.Globalization;
using System.Text.RegularExpressions;
using GapActivator.Domain.Common;

namespace GapActivator.Domain.Scheduling;

/// <summary>A validated, normalized iCalendar-style recurrence rule (RFC 5545 RRULE value).</summary>
public sealed partial record RecurrenceRule
{
    private static readonly HashSet<string> Frequencies =
        ["SECONDLY", "MINUTELY", "HOURLY", "DAILY", "WEEKLY", "MONTHLY", "YEARLY"];

    private static readonly HashSet<string> Keys =
    [
        "FREQ", "UNTIL", "COUNT", "INTERVAL", "BYSECOND", "BYMINUTE", "BYHOUR", "BYDAY",
        "BYMONTHDAY", "BYYEARDAY", "BYWEEKNO", "BYMONTH", "BYSETPOS", "WKST",
    ];

    private RecurrenceRule(string value) => Value = value;

    public string Value { get; }

    public static RecurrenceRule Parse(string value)
    {
        var normalized = (value ?? string.Empty).Trim().TrimEnd(';').ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new DomainException("Recurrence rule must not be empty.");
        }

        var parts = new Dictionary<string, string>();
        foreach (var part in normalized.Split(';'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1)
            {
                throw new DomainException($"Recurrence rule part '{part}' must have the form KEY=VALUE.");
            }

            var key = part[..separator];
            if (!Keys.Contains(key))
            {
                throw new DomainException($"Recurrence rule part '{key}' is not supported.");
            }

            if (!parts.TryAdd(key, part[(separator + 1)..]))
            {
                throw new DomainException($"Recurrence rule part '{key}' appears more than once.");
            }
        }

        if (!parts.TryGetValue("FREQ", out var frequency) || !Frequencies.Contains(frequency))
        {
            throw new DomainException("Recurrence rule requires a valid FREQ.");
        }

        if (parts.ContainsKey("COUNT") && parts.ContainsKey("UNTIL"))
        {
            throw new DomainException("Recurrence rule cannot contain both COUNT and UNTIL.");
        }

        RequirePositiveInteger(parts, "INTERVAL");
        RequirePositiveInteger(parts, "COUNT");

        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            foreach (var token in byDay.Split(','))
            {
                if (!ByDayToken().IsMatch(token))
                {
                    throw new DomainException($"BYDAY value '{token}' is not a valid weekday.");
                }
            }
        }

        if (parts.TryGetValue("WKST", out var weekStart) && !Weekday().IsMatch(weekStart))
        {
            throw new DomainException($"WKST value '{weekStart}' is not a valid weekday.");
        }

        return new RecurrenceRule(normalized);
    }

    public override string ToString() => Value;

    private static void RequirePositiveInteger(Dictionary<string, string> parts, string key)
    {
        if (parts.TryGetValue(key, out var text)
            && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1))
        {
            throw new DomainException($"{key} must be a positive integer.");
        }
    }

    [GeneratedRegex("^(MO|TU|WE|TH|FR|SA|SU)$")]
    private static partial Regex Weekday();

    [GeneratedRegex("^[+-]?[0-9]{0,2}(MO|TU|WE|TH|FR|SA|SU)$")]
    private static partial Regex ByDayToken();
}
