using GapActivator.Domain.Common;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Domain.Tests.Scheduling;

public class RecurrenceRuleTests
{
    [Fact]
    public void Parse_ValidRule_NormalizesToUpperCase()
    {
        var rule = RecurrenceRule.Parse("freq=weekly;interval=2;byday=mo,we,fr");

        rule.Value.ShouldBe("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR");
    }

    [Fact]
    public void Parse_SurroundingWhitespaceAndTrailingSemicolon_AreRemoved()
    {
        RecurrenceRule.Parse("  FREQ=DAILY;  ").Value.ShouldBe("FREQ=DAILY");
    }

    public static TheoryData<string> Frequencies => [.. RecurrenceRuleSamples.Frequencies];

    public static TheoryData<string> RangeBoundaries => [.. RecurrenceRuleSamples.RangeBoundaries];

    public static TheoryData<string> AllowedCombinations => [.. RecurrenceRuleSamples.AllowedCombinations];

    public static TheoryData<string> ReachableMonthAndDay => [.. RecurrenceRuleSamples.ReachableMonthAndDay];

    public static TheoryData<string> UntilForms => [.. RecurrenceRuleSamples.UntilForms];

    [Fact]
    public void Parse_OrdinalWeekdays_AreAccepted()
    {
        RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR,2MO;WKST=SU").Value
            .ShouldBe("FREQ=MONTHLY;BYDAY=-1FR,2MO;WKST=SU");
    }

    [Theory]
    [InlineData("")]
    [InlineData("INTERVAL=2")]
    [InlineData("FREQ=HOURLYY")]
    [InlineData("FREQ=DAILY;FREQ=DAILY")]
    [InlineData("FREQ=DAILY;COUNT=2;UNTIL=20270101T000000Z")]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=DAILY;COUNT=-1")]
    [InlineData("FREQ=DAILY;BYDAY=XX")]
    [InlineData("FREQ=DAILY;FOO=1")]
    [InlineData("FREQ=DAILY;INTERVAL")]
    public void Parse_InvalidRule_Throws(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value));
    }

    [Fact]
    public void Parse_SameRuleDifferentCase_AreEqual()
    {
        RecurrenceRule.Parse("freq=daily;byhour=8").ShouldBe(RecurrenceRule.Parse(" FREQ=DAILY;BYHOUR=8; "));
    }

    [Theory]
    [MemberData(nameof(UntilForms))]
    public void Parse_UntilDateOrUtcDateTimeWithoutYearRange_IsAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("FREQ=DAILY;;INTERVAL=2", "FREQ=DAILY;INTERVAL=2")]
    [InlineData(";FREQ=DAILY;; ;INTERVAL=2;;", "FREQ=DAILY;INTERVAL=2")]
    [InlineData("FREQ=DAILY;   ;COUNT=3", "FREQ=DAILY;COUNT=3")]
    public void Parse_EmptyOrWhitespaceParts_AreIgnoredAndNormalizedAway(string value, string expected)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(";")]
    [InlineData(" ; ;")]
    public void Parse_OnlyEmptyParts_Throws(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value));
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void Parse_SupportedFrequency_IsAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Parse_SecondlyFrequency_IsRejected()
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse("FREQ=SECONDLY"));
    }

    [Theory]
    [MemberData(nameof(RangeBoundaries))]
    public void Parse_ValuesOnRangeBoundaries_AreAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("FREQ=YEARLY;BYMONTH=0")]
    [InlineData("FREQ=YEARLY;BYMONTH=13")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,,2")]
    [InlineData("FREQ=YEARLY;BYMONTH=-1")]
    [InlineData("FREQ=YEARLY;BYMONTH=01")]
    [InlineData("FREQ=YEARLY;BYMONTH=1.5")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=0")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=+0")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=-0")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=32")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=-32")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=367")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-367")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=0")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=54")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-54")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=0")]
    [InlineData("FREQ=MONTHLY;BYHOUR=8;BYSETPOS=367")]
    [InlineData("FREQ=MONTHLY;BYHOUR=8;BYSETPOS=0")]
    [InlineData("FREQ=DAILY;BYSECOND=60")]
    [InlineData("FREQ=DAILY;BYSECOND=61")]
    [InlineData("FREQ=DAILY;BYSECOND=-1")]
    [InlineData("FREQ=DAILY;BYMINUTE=60")]
    [InlineData("FREQ=DAILY;BYHOUR=24")]
    [InlineData("FREQ=DAILY;BYHOUR=8,")]
    [InlineData("FREQ=DAILY;BYHOUR=99999999999")]
    [InlineData("FREQ=MONTHLY;BYDAY=0MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=54MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=-54MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=01MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,,TU")]
    [InlineData("FREQ=MONTHLY;BYDAY=+MO")]
    public void Parse_ValueOutOfRange_Throws(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value));
    }

    [Theory]
    [InlineData("FREQ=DAILY\n;INTERVAL=2")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO\n,TU")]
    [InlineData("FREQ=WEEKLY;WKST=MO\n;INTERVAL=2")]
    [InlineData("FREQ=DAILY;BYHOUR=8\n;INTERVAL=2")]
    [InlineData("FREQ=DAILY;UNTIL=20270301\n;INTERVAL=2")]
    public void Parse_NewlineInsideAValue_Throws(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value));
    }

    [Theory]
    [MemberData(nameof(UntilForms))]
    public void Parse_UntilRealDateOrUtcDateTime_IsAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("FREQ=DAILY;UNTIL=20270230")]
    [InlineData("FREQ=DAILY;UNTIL=20270229")]
    [InlineData("FREQ=DAILY;UNTIL=20271301")]
    [InlineData("FREQ=DAILY;UNTIL=20270228T246000Z")]
    [InlineData("FREQ=DAILY;UNTIL=20270228T235960Z")]
    [InlineData("FREQ=DAILY;UNTIL=20270228T000000")]
    [InlineData("FREQ=DAILY;UNTIL=20270228T0000Z")]
    [InlineData("FREQ=DAILY;UNTIL=2027-02-28")]
    [InlineData("FREQ=DAILY;UNTIL=2027022")]
    [InlineData("FREQ=DAILY;UNTIL=20270228Z")]
    [InlineData("FREQ=DAILY;UNTIL=NEVER")]
    public void Parse_UntilMalformedOrImpossible_Throws(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value));
    }

    [Theory]
    [MemberData(nameof(AllowedCombinations))]
    public void Parse_AllowedCombinations_AreAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYWEEKNO=20")]
    [InlineData("FREQ=DAILY;BYWEEKNO=20")]
    [InlineData("FREQ=DAILY;BYYEARDAY=1")]
    [InlineData("FREQ=WEEKLY;BYYEARDAY=1")]
    [InlineData("FREQ=MONTHLY;BYYEARDAY=1")]
    [InlineData("FREQ=WEEKLY;BYMONTHDAY=1")]
    [InlineData("FREQ=DAILY;BYDAY=2MO")]
    [InlineData("FREQ=WEEKLY;BYDAY=-1MO")]
    [InlineData("FREQ=HOURLY;BYDAY=+1MO")]
    [InlineData("FREQ=MONTHLY;BYSETPOS=1")]
    [InlineData("FREQ=DAILY;BYSETPOS=1;INTERVAL=2")]
    [InlineData("FREQ=DAILY;BYSETPOS=1;WKST=MO")]
    public void Parse_ForbiddenCombinations_Throw(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value));
    }

    [Theory]
    [MemberData(nameof(ReachableMonthAndDay))]
    public void Parse_ReachableMonthAndDay_IsAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-30")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-31,+30")]
    [InlineData("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=6,9,11;BYMONTHDAY=31")]
    [InlineData("FREQ=MINUTELY;BYMONTH=2;BYMONTHDAY=30")]
    public void Parse_UnreachableMonthAndDay_ThrowsNeverMatches(string value)
    {
        Should.Throw<DomainException>(() => RecurrenceRule.Parse(value))
            .Message.ShouldStartWith("Recurrence rule can never match:");
    }
}
