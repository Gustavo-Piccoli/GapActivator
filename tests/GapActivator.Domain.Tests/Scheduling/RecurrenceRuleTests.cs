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
}
