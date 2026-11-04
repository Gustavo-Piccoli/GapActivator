using Avalonia.Styling;
using GapActivator.App.Localization;
using GapActivator.App.Settings;

namespace GapActivator.App.Services;

/// <summary>Applies the theme and language from the settings now and whenever they change.</summary>
public sealed class ThemeApplier(Localizer localizer, IAppSettingsService settings)
{
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Apply(settings.Current);
        settings.Changed += (_, current) => Apply(current);
    }

    private void Apply(AppSettings current)
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = current.Theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        localizer.SetLanguage(current.Language);
    }
}
