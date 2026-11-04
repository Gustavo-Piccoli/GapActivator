using GapActivator.Application;
using GapActivator.Application.Ports;
using GapActivator.Infrastructure;
using GapActivator.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service;

/// <summary>Composes the GapActivator service host.</summary>
public static class GapActivatorHost
{
    /// <summary>
    /// The host builder. The data root comes from the configuration key <see cref="GapActivatorPaths.DataRootKey"/>
    /// (command line <c>--GapActivator:DataRoot=&lt;path&gt;</c> or env var <c>GapActivator__DataRoot</c>), or is
    /// <c>%ProgramData%\GapActivator</c>. Under the Service Control Manager the host runs with the service lifetime and
    /// logs to the daily file and the Event Log; otherwise it also logs to the console.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        string? dataRoot = builder.Configuration[GapActivatorPaths.DataRootKey];
        GapActivatorPaths paths = dataRoot is null ? GapActivatorPaths.Default : new GapActivatorPaths(dataRoot);

        bool isService = WindowsServiceHelpers.IsWindowsService();
        builder.Services.AddWindowsService(o => o.ServiceName = "GapActivator");
        if (isService)
        {
            builder.Services.RemoveAll<IHostLifetime>();
            builder.Services.AddSingleton<IHostLifetime, GapActivatorServiceLifetime>();
        }

        builder.Services.Configure<HostOptions>(o =>
        {
            o.ShutdownTimeout = ServiceTimeouts.Shutdown;
            o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        // One switch for the file logger and the persistence (which only adds its own when none is registered), so a
        // log level saved in the settings takes effect at once.
        var level = new LogLevelSwitch();
        builder.Services.AddSingleton(level);
        builder.Logging.AddGapActivatorLogging(paths, level, TimeProvider.System);
        if (!isService)
        {
            // The console logger ships with the host; the GapActivator logging cleared the default providers.
            builder.Logging.AddConsole();
        }

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(new InfrastructureOptions(paths));

        builder.Services.AddSingleton(new ExitCodeSink(code => Environment.ExitCode = code));
        builder.Services.AddSingleton<HostRestart>();
        builder.Services.AddScoped<IServiceControl, ScopedServiceControl>();
        builder.Services.AddHostedService<GapActivatorWorker>();
        return builder;
    }

    public static IHost Build(string[] args) => CreateBuilder(args).Build();
}
