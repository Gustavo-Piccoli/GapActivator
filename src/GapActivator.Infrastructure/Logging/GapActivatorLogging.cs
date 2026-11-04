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
        TimeProvider time) =>
        logging.AddGapActivatorLogging(paths, level, time, TryCreateEventLog);

    /// <summary>The seam for tests: <paramref name="createEventLog"/> supplies the provider the safe wrapper delegates to.</summary>
    internal static ILoggingBuilder AddGapActivatorLogging(
        this ILoggingBuilder logging,
        GapActivatorPaths paths,
        LogLevelSwitch level,
        TimeProvider time,
        Func<ILoggerProvider?> createEventLog)
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

        ILoggerProvider? eventLog = createEventLog();
        if (eventLog is not null)
        {
            logging.AddProvider(new SafeEventLogLoggerProvider(eventLog));
        }

        // The rules are added in every case, so the policy is the same everywhere (BI-090, spec section 9).
        logging.AddFilter<SafeEventLogLoggerProvider>(null, LogLevel.Error);
        logging.AddFilter<SafeEventLogLoggerProvider>(NotifierCategory, LogLevel.None);
        return logging;
    }

    private static ILoggerProvider? TryCreateEventLog()
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
