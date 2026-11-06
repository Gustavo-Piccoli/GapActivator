using Avalonia.Styling;
using GapActivator.App.Localization;
using GapActivator.App.Settings;

namespace GapActivator.App.Services;

/// <summary>Applies the theme, the language and its culture from the settings now and whenever they change.</summary>
public sealed class ThemeApplier(Localizer localizer, IAppSettingsService settings, IUiDispatcher ui, ICultureApplier culture)
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
        // Changed can be raised from any thread; Avalonia and the localizer's bindings need the UI thread.
        settings.Changed += (_, current) => ui.Post(() => Apply(current));
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

        // The date pickers format with the thread culture, so it follows the app language.
        culture.Apply(localizer.Culture);
    }
}
