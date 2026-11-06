using System.Globalization;

namespace GapActivator.App.Services;

/// <summary>Sets the culture the UI thread and new threads format with. A seam, so tests never touch the process culture.</summary>
public interface ICultureApplier
{
    void Apply(CultureInfo culture);
}

/// <summary>
/// Makes the app language the culture of the process: the date pickers format with the thread culture, not with the localizer.
/// </summary>
public sealed class ProcessCultureApplier : ICultureApplier
{
    public void Apply(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
