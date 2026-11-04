using System.Globalization;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Shell;
using GapActivator.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

        AddGateway(services);

        overrides?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static void AddGateway(IServiceCollection services)
    {
        services.AddSingleton(_ => new GapActivatorClientOptions { ClientVersion = AppVersion.Display });
        services.AddSingleton<IServiceStatusProbe>(_ => new ScmServiceStatusProbe(IpcProtocol.ServiceName));

        // Task 3 replaces the null logger with the app log.
        services.AddSingleton(sp => new ServiceGateway(
            sp.GetRequiredService<GapActivatorClientOptions>(),
            sp.GetRequiredService<IServiceStatusProbe>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<ServiceGateway>.Instance));
        services.AddSingleton<IServiceGateway>(sp => sp.GetRequiredService<ServiceGateway>());
    }
}
