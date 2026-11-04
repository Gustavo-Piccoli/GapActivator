using System.Globalization;
using GapActivator.App.Localization;

namespace GapActivator.App.Settings;

public sealed record AppSettings(
    AppTheme Theme,
    AppLanguage Language,
    bool Notifications,
    bool CheckForUpdates,
    DateTimeOffset? LastUpdateCheck,
    WindowBounds? Window)
{
    public static AppSettings Defaults(CultureInfo uiCulture) =>
        new(AppTheme.System, Localizer.DefaultLanguage(uiCulture), true, true, null, null);
}
