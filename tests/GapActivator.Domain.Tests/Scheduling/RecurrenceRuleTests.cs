using GapActivator.Domain.Common;
using GapActivator.Domain.Scheduling;

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
    [InlineData("FREQ=DAILY;UNTIL=20270301")]
    [InlineData("FREQ=DAILY;UNTIL=20270301T000000Z")]
    [InlineData("FREQ=DAILY;UNTIL=21501231T235959Z")]
    [InlineData("FREQ=DAILY;UNTIL=18991231")]
    public void Parse_UntilDateOrUtcDateTimeWithoutYearRange_IsAccepted(string value)
    {
        RecurrenceRule.Parse(value).Value.ShouldBe(value);
    }
}
