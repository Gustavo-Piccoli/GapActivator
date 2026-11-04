using GapActivator.Application.Ports;
using GapActivator.Domain.Scheduling;
using GapActivator.Infrastructure.Email;
using GapActivator.Infrastructure.Files;
using GapActivator.Infrastructure.Notifications;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Infrastructure.Processes;
using GapActivator.Infrastructure.Scheduling;
using GapActivator.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the infrastructure adapters: the scoped persistence and the long-lived singletons (launcher, secret
    /// protector, log retention, output reader, e-mail sender, notifier and occurrence calculator).
    /// <c>IServiceControl</c> is not registered here: it belongs to the host. Logging is added by the host as well.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddPersistence(options.Paths);

        services.AddSingleton(options);
        services.AddSingleton(options.Email);
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ILogRetention, FileLogRetention>();
        services.AddSingleton<IRunOutputReader, RunOutputReader>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IAppNotifier, LoggingAppNotifier>();
        services.AddSingleton<IOccurrenceCalculator>(_ => new IcalOccurrenceCalculator(TimeZoneInfo.Local));
        return services;
    }
}
