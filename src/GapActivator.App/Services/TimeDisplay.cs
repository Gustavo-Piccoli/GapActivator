using GapActivator.App.Localization;

namespace GapActivator.App.Services;

/// <summary>Formats instants for the screen: in the local zone, in the language's culture.</summary>
public sealed class TimeDisplay(Localizer l, TimeZoneInfo zone)
{
    public string DateTime(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, zone).ToString("g", l.Culture);

    public string WithWeekday(DateTimeOffset at)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(at, zone);
        return $"{local.ToString("ddd", l.Culture)} {local.ToString("g", l.Culture)}";
    }

    public string Time(TimeOnly time) => time.ToString("t", l.Culture);
}
