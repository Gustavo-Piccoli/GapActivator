using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Services;

public class TimeDisplayTests
{
    private static readonly TimeZoneInfo UtcPlus3 = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    [Fact]
    public void TimeDisplay_FormatsInTheZoneAndCulture()
    {
        var l = new Localizer(AppLanguage.English);
        var times = new TimeDisplay(l, UtcPlus3);

        var english = times.DateTime(Dtos.Epoch);
        l.SetLanguage(AppLanguage.Portuguese);
        var portuguese = times.DateTime(Dtos.Epoch);

        // Newer ICU data separates the day period with a narrow no-break space.
        english.Replace(' ', ' ').ShouldBe("1/1/2030 3:00 PM");
        portuguese.ShouldBe("01/01/2030 15:00");
    }

    [Fact]
    public void WithWeekday_PrefixesTheAbbreviatedDay()
    {
        var l = new Localizer(AppLanguage.English);
        var times = new TimeDisplay(l, UtcPlus3);

        times.WithWeekday(Dtos.Epoch).Replace(' ', ' ').ShouldBe("Tue 1/1/2030 3:00 PM");
    }

    [Fact]
    public void Time_UsesTheShortTimePattern()
    {
        var l = new Localizer(AppLanguage.Portuguese);

        new TimeDisplay(l, UtcPlus3).Time(new TimeOnly(8, 5)).ShouldBe("08:05");
    }

    [Theory]
    [InlineData(400, "< 1 s")]
    [InlineData(45_000, "45 s")]
    [InlineData(125_000, "2 min 5 s")]
    [InlineData(3_780_000, "1 h 3 min")]
    public void Duration_Format(int milliseconds, string expected)
    {
        var l = new Localizer(AppLanguage.English);

        Durations.Format(TimeSpan.FromMilliseconds(milliseconds), l).ShouldBe(expected);
        l.SetLanguage(AppLanguage.Portuguese);
        Durations.Format(TimeSpan.FromMilliseconds(milliseconds), l).ShouldBe(expected);
    }
}
