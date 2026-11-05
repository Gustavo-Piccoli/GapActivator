using System.Globalization;
using GapActivator.App.Localization;
using GapActivator.App.Logging;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Shell;
using GapActivator.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddSingleton(sp => new Localizer(sp.GetRequiredService<IAppSettingsService>().Current.Language));
        services.AddSingleton<IUiDispatcher>(_ => new AvaloniaUiDispatcher());

        AddLogging(services);
        AddSettings(services);
        AddGateway(services);
        AddShell(services);

        overrides?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static void AddLogging(IServiceCollection services)
    {
        services.AddSingleton(sp => new AppLogFile(sp.GetRequiredService<AppPaths>().LogsDirectory, sp.GetRequiredService<TimeProvider>()));
        Logger<App>(services);
        Logger<AppSettingsStore>(services);
        Logger<ServiceGateway>(services);
    }

    private static void Logger<T>(IServiceCollection services) =>
        services.AddSingleton<ILogger<T>>(sp => new AppLogger<T>(sp.GetRequiredService<AppLogFile>(), sp.GetRequiredService<TimeProvider>()));

    private static void AddSettings(IServiceCollection services)
    {
        services.AddSingleton(sp => new AppSettingsStore(
            sp.GetRequiredService<AppPaths>().SettingsFile,
            sp.GetRequiredService<ILogger<AppSettingsStore>>(),
            CultureInfo.CurrentUICulture));
        services.AddSingleton<IAppSettingsService>(sp =>
        {
            var store = sp.GetRequiredService<AppSettingsStore>();
            return new AppSettingsService(store, store.Load());
        });
        services.AddSingleton(sp => new ThemeApplier(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<IUiDispatcher>()));
    }

    private static void AddGateway(IServiceCollection services)
    {
        services.AddSingleton(_ => new GapActivatorClientOptions { ClientVersion = AppVersion.Display });
        services.AddSingleton<IServiceStatusProbe>(_ => new ScmServiceStatusProbe(IpcProtocol.ServiceName));

        services.AddSingleton(sp => new ServiceGateway(
            sp.GetRequiredService<GapActivatorClientOptions>(),
            sp.GetRequiredService<IServiceStatusProbe>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ServiceGateway>>()));
        services.AddSingleton<IServiceGateway>(sp => sp.GetRequiredService<ServiceGateway>());
    }

    private static void AddShell(IServiceCollection services)
    {
        services.AddSingleton(_ => new ShellWindow());
        services.AddSingleton<ITrayIcon>(sp => new Win32TrayIcon(
            sp.GetRequiredService<ShellWindow>(),
            TrayIconImages.Colors.ToDictionary(c => c.Key, c => TrayIconImages.Create(c.Value))));
    }
}
