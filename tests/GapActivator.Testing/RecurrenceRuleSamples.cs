namespace GapActivator.Testing;

/// <summary>
/// Recurrence rules the domain accepts, written in their normalized form. Shared so the domain tests prove they parse
/// and the infrastructure tests prove the occurrence calculator can expand every one of them.
/// </summary>
public static class RecurrenceRuleSamples
{
    public static IReadOnlyList<string> Frequencies { get; } =
    [
        "FREQ=MINUTELY", "FREQ=HOURLY", "FREQ=DAILY", "FREQ=WEEKLY", "FREQ=MONTHLY", "FREQ=YEARLY",
    ];

    public static IReadOnlyList<string> RangeBoundaries { get; } =
    [
        "FREQ=YEARLY;BYMONTH=1,12",
        "FREQ=YEARLY;BYMONTHDAY=1,31,-1,-31,+5",
        "FREQ=YEARLY;BYYEARDAY=1,366,-1,-366",
        "FREQ=YEARLY;BYWEEKNO=1,53,-1,-53",
        "FREQ=MONTHLY;BYHOUR=8;BYSETPOS=1,366,-1,-366",
        "FREQ=DAILY;BYSECOND=0,59",
        "FREQ=DAILY;BYMINUTE=0,59",
        "FREQ=DAILY;BYHOUR=0,23",
        "FREQ=MONTHLY;BYDAY=1MO,+53SU,-53SA,TU,-1FR",
    ];

    public static IReadOnlyList<string> AllowedCombinations { get; } =
    [
        "FREQ=YEARLY;BYWEEKNO=20",
        "FREQ=HOURLY;BYYEARDAY=100",
        "FREQ=YEARLY;BYYEARDAY=100",
        "FREQ=DAILY;BYMONTHDAY=1",
        "FREQ=MONTHLY;BYMONTHDAY=1",
        "FREQ=WEEKLY;BYDAY=MO,FR",
        "FREQ=MONTHLY;BYDAY=2TU",
        "FREQ=YEARLY;BYDAY=-1SU",
        "FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1",
        "FREQ=MONTHLY;BYMONTHDAY=1,15;BYSETPOS=1",
        "FREQ=MONTHLY;BYDAY=-1FR,2MO;WKST=SU",
    ];

    public static IReadOnlyList<string> ReachableMonthAndDay { get; } =
    [
        "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29",
        "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-29",
        "FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=30",
        "FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=31",
        "FREQ=YEARLY;BYMONTH=2,3;BYMONTHDAY=31",
        "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30,15",
        "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-30,-1",
    ];

    public static IReadOnlyList<string> UntilForms { get; } =
    [
        "FREQ=DAILY;UNTIL=20270301",
        "FREQ=DAILY;UNTIL=20270301T000000Z",
        "FREQ=DAILY;UNTIL=21501231T235959Z",
        "FREQ=DAILY;UNTIL=18991231",
        "FREQ=DAILY;UNTIL=20280229",
        "FREQ=DAILY;UNTIL=20271231T235959Z",
    ];

    public static IReadOnlyList<string> Accepted { get; } =
    [
        .. Frequencies,
        .. RangeBoundaries,
        .. AllowedCombinations,
        .. ReachableMonthAndDay,
        .. UntilForms,
    ];
}
