using System.Globalization;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Scheduling;

/// <summary>Turns the friendly modes into the service's rule and anchor, and reads a stored rule back into a friendly mode when it can.</summary>
internal static class ScheduleRules
{
    public const int MaxMinutes = 1440;
    public const int MaxHours = 168;

    private static readonly (DayOfWeek Day, string Code)[] Days =
    [
        (DayOfWeek.Monday, "MO"),
        (DayOfWeek.Tuesday, "TU"),
        (DayOfWeek.Wednesday, "WE"),
        (DayOfWeek.Thursday, "TH"),
        (DayOfWeek.Friday, "FR"),
        (DayOfWeek.Saturday, "SA"),
        (DayOfWeek.Sunday, "SU"),
    ];

    /// <summary>Generates the rule and the anchor. Numbers carry no leading zeros: the service's parser rejects them.</summary>
    public static ScheduleInputDto ToInput(FriendlySchedule schedule, Guid? id, TimeZoneInfo zone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(zone);

        string at = string.Create(CultureInfo.InvariantCulture, $"BYHOUR={schedule.Time.Hour};BYMINUTE={schedule.Time.Minute}");
        DateTime todayAtTime = today.ToDateTime(new TimeOnly(schedule.Time.Hour, schedule.Time.Minute));

        (string? rule, DateTime local) = Plan(schedule, at, todayAtTime);
        ScheduleInputKind kind = schedule.Mode == ScheduleMode.Once ? ScheduleInputKind.OneTime : ScheduleInputKind.Recurring;
        return new ScheduleInputDto(id, kind, LocalTime.ToOffset(local, zone), rule);
    }

    private static (string? Rule, DateTime Local) Plan(FriendlySchedule schedule, string at, DateTime todayAtTime) =>
        schedule.Mode switch
        {
            ScheduleMode.EveryMinutes => (Interval("MINUTELY", schedule.Interval), WholeMinute(schedule.Start)),
            ScheduleMode.EveryHours => (Interval("HOURLY", schedule.Interval), WholeMinute(schedule.Start)),
            ScheduleMode.Daily => ($"FREQ=DAILY;{at}", todayAtTime),
            ScheduleMode.Weekly => ($"FREQ=WEEKLY;BYDAY={DayList(schedule.Days)};{at}", todayAtTime),
            ScheduleMode.Monthly => (string.Create(CultureInfo.InvariantCulture, $"FREQ=MONTHLY;BYMONTHDAY={schedule.DayOfMonth};{at}"), todayAtTime),
            ScheduleMode.Once => (null, schedule.Start),
            ScheduleMode.Advanced => ((schedule.Rule ?? string.Empty).Trim(), schedule.Start),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule)),
        };

    /// <summary>Reads a stored schedule. It opens in a friendly mode only when that mode regenerates the same rule; anything else is Advanced.</summary>
    public static FriendlySchedule FromView(ScheduleInputKind kind, DateTimeOffset at, string? rule, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTime local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(at, zone).DateTime, DateTimeKind.Unspecified);

        if (kind == ScheduleInputKind.OneTime)
        {
            return new FriendlySchedule(ScheduleMode.Once, 1, TimeOnly.FromDateTime(local), [], 1, local, null);
        }

        FriendlySchedule advanced = new(ScheduleMode.Advanced, 1, TimeOnly.FromDateTime(local), [], 1, local, rule ?? string.Empty);
        if (rule is null || local.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            return advanced;
        }

        Dictionary<string, string>? pairs = Split(rule);
        if (pairs is null || !pairs.TryGetValue("FREQ", out string? freq))
        {
            return advanced;
        }

        FriendlySchedule? friendly = freq switch
        {
            "MINUTELY" => ReadInterval(pairs, ScheduleMode.EveryMinutes, MaxMinutes, local),
            "HOURLY" => ReadInterval(pairs, ScheduleMode.EveryHours, MaxHours, local),
            "DAILY" => ReadAt(pairs, ["FREQ", "BYHOUR", "BYMINUTE"], ScheduleMode.Daily, local),
            "WEEKLY" => ReadAt(pairs, ["FREQ", "BYDAY", "BYHOUR", "BYMINUTE"], ScheduleMode.Weekly, local),
            "MONTHLY" => ReadAt(pairs, ["FREQ", "BYMONTHDAY", "BYHOUR", "BYMINUTE"], ScheduleMode.Monthly, local),
            _ => null,
        };
        return friendly ?? advanced;
    }

    private static string Interval(string frequency, int interval) =>
        interval > 1 ? string.Create(CultureInfo.InvariantCulture, $"FREQ={frequency};INTERVAL={interval}") : $"FREQ={frequency}";

    private static DateTime WholeMinute(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

    private static string DayList(IReadOnlyList<DayOfWeek> days)
    {
        string[] codes = [.. Days.Where(d => days.Contains(d.Day)).Select(d => d.Code)];
        return codes.Length == 0 ? throw new InvalidOperationException("A weekly schedule needs at least one day.") : string.Join(',', codes);
    }

    private static Dictionary<string, string>? Split(string rule)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in rule.Split(';'))
        {
            string[] keyValue = part.Split('=');
            if (keyValue.Length != 2 || !pairs.TryAdd(keyValue[0].Trim().ToUpperInvariant(), keyValue[1].Trim().ToUpperInvariant()))
            {
                return null;
            }
        }

        return pairs;
    }

    // A number is canonical when it prints back as written: no sign, no space, no leading zero.
    private static bool TryNumber(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value >= min
        && value <= max
        && value.ToString(CultureInfo.InvariantCulture) == text;

    private static FriendlySchedule? ReadInterval(Dictionary<string, string> pairs, ScheduleMode mode, int max, DateTime local)
    {
        if (pairs.Keys.Any(k => k is not ("FREQ" or "INTERVAL")))
        {
            return null;
        }

        var interval = 1;
        if (pairs.TryGetValue("INTERVAL", out string? text) && !TryNumber(text, 1, max, out interval))
        {
            return null;
        }

        return new FriendlySchedule(mode, interval, TimeOnly.FromDateTime(local), [], 1, local, null);
    }

    private static FriendlySchedule? ReadAt(Dictionary<string, string> pairs, string[] keys, ScheduleMode mode, DateTime local)
    {
        if (pairs.Count != keys.Length || keys.Any(k => !pairs.ContainsKey(k)))
        {
            return null;
        }

        if (!TryNumber(pairs["BYHOUR"], 0, 23, out int hour) || !TryNumber(pairs["BYMINUTE"], 0, 59, out int minute))
        {
            return null;
        }

        List<DayOfWeek> days = [];
        var dayOfMonth = 1;
        if (mode == ScheduleMode.Weekly)
        {
            var seen = new HashSet<DayOfWeek>();
            foreach (string code in pairs["BYDAY"].Split(','))
            {
                int index = Array.FindIndex(Days, d => d.Code == code);
                if (index < 0 || !seen.Add(Days[index].Day))
                {
                    return null;
                }
            }

            days = [.. Days.Where(d => seen.Contains(d.Day)).Select(d => d.Day)];
        }
        else if (mode == ScheduleMode.Monthly && !TryNumber(pairs["BYMONTHDAY"], 1, 31, out dayOfMonth))
        {
            return null;
        }

        return new FriendlySchedule(mode, 1, new TimeOnly(hour, minute), days, dayOfMonth, local, null);
    }
}
