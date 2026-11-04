using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.EventLog;

namespace GapActivator.Infrastructure.Logging;

/// <summary>Registers the service logging: the daily file and, for critical and curated errors only, the Windows Event Log.</summary>
public static class GapActivatorLogging
{
    public const string EventSource = "GapActivator";

    /// <summary>Error entries of these categories reach the Event Log; every other category only Critical (BI-090).</summary>
    public static readonly IReadOnlyList<string> EventLogErrorCategories =
        ["GapActivator.Application.Cycle.SchedulerLoop", "GapActivator.Application.Runs.RunSupervisor"];

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

        // Factory registrations, so the container owns the providers and disposing the host closes the live file. An
        // instance passed to AddProvider is disposed by neither the container nor the logger factory.
        logging.Services.AddSingleton<ILoggerProvider>(_ => new DailyFileLoggerProvider(paths, level, time));

        // The inner provider is created inside the factory, so each container gets (and disposes) its own. When it
        // cannot be created, the wrapper logs nowhere.
        logging.Services.AddSingleton<ILoggerProvider>(_ =>
            new SafeEventLogLoggerProvider(createEventLog() ?? NullLoggerProvider.Instance, new EventLogRateLimiter(time)));

        // The rules are added in every case, so the policy is the same everywhere (BI-090, spec section 9).
        logging.AddFilter<SafeEventLogLoggerProvider>(null, LogLevel.Critical);
        foreach (string category in EventLogErrorCategories)
        {
            logging.AddFilter<SafeEventLogLoggerProvider>(category, LogLevel.Error);
        }

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
