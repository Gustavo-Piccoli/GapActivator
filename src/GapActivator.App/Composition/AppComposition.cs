using System.Globalization;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.App.Composition;

/// <summary>The composition root. Only explicit singleton factories, so trimming and Native AOT are safe.</summary>
public static class AppComposition
{
    public static ServiceProvider Build(AppPaths paths, StartupOptions options, Action<IServiceCollection>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(TimeZoneInfo.Local);
        services.AddSingleton(_ => new Localizer(Localizer.DefaultLanguage(CultureInfo.CurrentUICulture)));
        services.AddSingleton<IUiDispatcher>(_ => new AvaloniaUiDispatcher());

        overrides?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
