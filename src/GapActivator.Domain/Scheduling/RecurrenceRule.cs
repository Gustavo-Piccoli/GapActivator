using System.Globalization;
using System.Text.RegularExpressions;
using GapActivator.Domain.Common;

namespace GapActivator.Domain.Scheduling;

/// <summary>A validated, normalized iCalendar-style recurrence rule (RFC 5545 RRULE value).</summary>
public sealed partial record RecurrenceRule
{
    private static readonly HashSet<string> Frequencies =
        ["MINUTELY", "HOURLY", "DAILY", "WEEKLY", "MONTHLY", "YEARLY"];

    private static readonly HashSet<string> Keys =
    [
        "FREQ", "UNTIL", "COUNT", "INTERVAL", "BYSECOND", "BYMINUTE", "BYHOUR", "BYDAY",
        "BYMONTHDAY", "BYYEARDAY", "BYWEEKNO", "BYMONTH", "BYSETPOS", "WKST",
    ];

    private static readonly string[] OtherByParts =
        ["BYSECOND", "BYMINUTE", "BYHOUR", "BYDAY", "BYMONTHDAY", "BYYEARDAY", "BYWEEKNO", "BYMONTH"];

    // Longest possible month (February counts its leap day), used to detect unreachable day/month pairs.
    private static readonly int[] MaxDaysInMonth = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    private RecurrenceRule(string value) => Value = value;

    public string Value { get; }

    public static RecurrenceRule Parse(string value)
    {
        var parts = new Dictionary<string, string>();
        var kept = new List<string>();
        foreach (var part in (value ?? string.Empty).Trim().ToUpperInvariant().Split(';'))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

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

            kept.Add(part);
        }

        if (kept.Count == 0)
        {
            throw new DomainException("Recurrence rule must not be empty.");
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
        ValidateUntil(parts);
        ValidateByParts(parts);

        if (parts.TryGetValue("WKST", out var weekStart) && !Weekday().IsMatch(weekStart))
        {
            throw new DomainException($"WKST value '{weekStart}' is not a valid weekday.");
        }

        ValidateCombinations(parts, frequency);

        return new RecurrenceRule(string.Join(';', kept));
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

    private static void ValidateUntil(Dictionary<string, string> parts)
    {
        if (!parts.TryGetValue("UNTIL", out var until))
        {
            return;
        }

        var valid = UntilDate().IsMatch(until)
            ? DateTime.TryParseExact(until, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            : UntilDateTime().IsMatch(until)
                && DateTime.TryParseExact(until, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        if (!valid)
        {
            throw new DomainException(
                $"UNTIL value '{until}' must be a real date (YYYYMMDD) or UTC date-time (YYYYMMDDTHHMMSSZ).");
        }
    }

    private static void ValidateByParts(Dictionary<string, string> parts)
    {
        ValidateNumbers(parts, "BYMONTH", 1, 12, signed: false);
        ValidateNumbers(parts, "BYMONTHDAY", 1, 31, signed: true);
        ValidateNumbers(parts, "BYYEARDAY", 1, 366, signed: true);
        ValidateNumbers(parts, "BYWEEKNO", 1, 53, signed: true);
        ValidateNumbers(parts, "BYSETPOS", 1, 366, signed: true);
        // 0..59 only: a leap second (60) cannot be expanded by the occurrence calculator.
        ValidateNumbers(parts, "BYSECOND", 0, 59, signed: false);
        ValidateNumbers(parts, "BYMINUTE", 0, 59, signed: false);
        ValidateNumbers(parts, "BYHOUR", 0, 23, signed: false);

        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            foreach (var token in byDay.Split(','))
            {
                var match = ByDayToken().Match(token);
                if (!match.Success
                    || (match.Groups["ordinal"].Success && Magnitude(match.Groups["ordinal"].Value) > 53))
                {
                    throw new DomainException($"BYDAY value '{token}' is not a valid weekday.");
                }
            }
        }
    }

    // An unsigned item is a plain integer in [min, max]; a signed one is an optionally signed integer
    // whose magnitude is in [min, max] (min is 1, so zero is invalid). Leading zeros are rejected.
    private static void ValidateNumbers(Dictionary<string, string> parts, string key, int min, int max, bool signed)
    {
        if (!parts.TryGetValue(key, out var list))
        {
            return;
        }

        foreach (var item in list.Split(','))
        {
            if (!(signed ? SignedInteger() : UnsignedInteger()).IsMatch(item) || Magnitude(item) < min || Magnitude(item) > max)
            {
                throw new DomainException($"{key} value '{item}' must be between {min} and {max}.");
            }
        }
    }

    private static void ValidateCombinations(Dictionary<string, string> parts, string frequency)
    {
        if (parts.ContainsKey("BYWEEKNO") && frequency != "YEARLY")
        {
            throw new DomainException("BYWEEKNO is only allowed with FREQ=YEARLY.");
        }

        if (parts.ContainsKey("BYYEARDAY") && frequency is "DAILY" or "WEEKLY" or "MONTHLY")
        {
            throw new DomainException("BYYEARDAY is not allowed with FREQ=DAILY, WEEKLY or MONTHLY.");
        }

        if (parts.ContainsKey("BYMONTHDAY") && frequency == "WEEKLY")
        {
            throw new DomainException("BYMONTHDAY is not allowed with FREQ=WEEKLY.");
        }

        // BYDAY tokens are already validated: anything longer than the two-letter weekday carries an ordinal.
        if (parts.TryGetValue("BYDAY", out var byDay)
            && frequency is not ("MONTHLY" or "YEARLY")
            && byDay.Split(',').Any(token => token.Length > 2))
        {
            throw new DomainException("A BYDAY ordinal is only allowed with FREQ=MONTHLY or YEARLY.");
        }

        // RFC 5545: with FREQ=YEARLY and BYWEEKNO, a BYDAY ordinal is not allowed.
        if (frequency == "YEARLY"
            && parts.ContainsKey("BYWEEKNO")
            && parts.TryGetValue("BYDAY", out var weekDays)
            && weekDays.Split(',').Any(token => token.Length > 2))
        {
            throw new DomainException("A BYDAY ordinal is not allowed together with BYWEEKNO.");
        }

        if (parts.ContainsKey("BYSETPOS") && !OtherByParts.Any(parts.ContainsKey))
        {
            throw new DomainException("BYSETPOS requires at least one other BY* part.");
        }

        if (parts.TryGetValue("BYMONTH", out var months) && parts.TryGetValue("BYMONTHDAY", out var days))
        {
            var monthNumbers = months.Split(',').Select(Magnitude).ToArray();

            // A negative day counts from the month end, so only its magnitude matters.
            var dayMagnitudes = days.Split(',').Select(Magnitude).ToArray();

            if (!monthNumbers.Any(month => dayMagnitudes.Any(day => day <= MaxDaysInMonth[month - 1])))
            {
                throw new DomainException(
                    $"Recurrence rule can never match: BYMONTHDAY={days} does not exist in BYMONTH={months}.");
            }
        }
    }

    // Callers have already matched the item against a pattern that bounds it to a few digits.
    private static int Magnitude(string item) =>
        int.Parse(item.TrimStart('+', '-'), NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(MO|TU|WE|TH|FR|SA|SU)\z")]
    private static partial Regex Weekday();

    [GeneratedRegex(@"^(?<ordinal>[+-]?[1-9][0-9]?)?(MO|TU|WE|TH|FR|SA|SU)\z")]
    private static partial Regex ByDayToken();

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,2})\z")]
    private static partial Regex UnsignedInteger();

    [GeneratedRegex(@"^[+-]?(0|[1-9][0-9]{0,2})\z")]
    private static partial Regex SignedInteger();

    [GeneratedRegex(@"^[0-9]{8}\z")]
    private static partial Regex UntilDate();

    [GeneratedRegex(@"^[0-9]{8}T[0-9]{6}Z\z")]
    private static partial Regex UntilDateTime();
}
