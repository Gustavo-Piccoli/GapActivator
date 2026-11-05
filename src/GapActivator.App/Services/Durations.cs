using GapActivator.App.Localization;

namespace GapActivator.App.Services;

/// <summary>Short duration text. The unit symbols are the same in both languages; only the digits follow the culture.</summary>
internal static class Durations
{
    public static string Format(TimeSpan duration, Localizer l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var culture = l.Culture;
        if (duration < TimeSpan.FromSeconds(1))
        {
            return "< 1 s";
        }

        if (duration < TimeSpan.FromMinutes(1))
        {
            return $"{duration.Seconds.ToString(culture)} s";
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return $"{duration.Minutes.ToString(culture)} min {duration.Seconds.ToString(culture)} s";
        }

        var hours = (int)duration.TotalHours;
        return $"{hours.ToString(culture)} h {duration.Minutes.ToString(culture)} min";
    }
}
