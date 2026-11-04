using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

namespace GapActivator.Infrastructure.Logging;

/// <summary>Registers the service logging: the daily file and, for severe errors only, the Windows Event Log.</summary>
public static class GapActivatorLogging
{
    public const string EventSource = "GapActivator";

    internal const string NotifierCategory = "GapActivator.Infrastructure.Notifications.LoggingAppNotifier";

    /// <remarks>Adds no console logger: the host decides about console output.</remarks>
    public static ILoggingBuilder AddGapActivatorLogging(
        this ILoggingBuilder logging,
        GapActivatorPaths paths,
        LogLevelSwitch level,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(time);

        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        logging.AddProvider(new DailyFileLoggerProvider(paths, level, time));

        ILoggerProvider? eventLog = TryCreateEventLog();
        if (eventLog is not null)
        {
            logging.AddProvider(new SafeEventLogLoggerProvider(eventLog));
        }

        // The rules are added in every case, so the policy is the same everywhere (BI-090, spec section 9).
        string eventLogProvider = typeof(SafeEventLogLoggerProvider).FullName!;
        logging.Services.Configure<LoggerFilterOptions>(
            options =>
            {
                options.Rules.Add(new LoggerFilterRule(eventLogProvider, null, LogLevel.Error, null));
                options.Rules.Add(new LoggerFilterRule(eventLogProvider, NotifierCategory, LogLevel.None, null));
            });
        return logging;
    }

    private static EventLogLoggerProvider? TryCreateEventLog()
    {
        try
        {
            return new EventLogLoggerProvider(new EventLogSettings { SourceName = EventSource, LogName = "Application" });
        }
        catch (Exception)
        {
            return null;
        }
    }
}
