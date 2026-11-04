using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Logging;

/// <summary>Wraps the Event Log provider so that a missing source or a denied write never reaches the caller.</summary>
[ProviderAlias("EventLog")]
internal sealed class SafeEventLogLoggerProvider(ILoggerProvider inner) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        try
        {
            return new SafeLogger(inner.CreateLogger(categoryName));
        }
        catch (Exception)
        {
            return new SafeLogger(null);
        }
    }

    public void Dispose()
    {
        try
        {
            inner.Dispose();
        }
        catch (Exception)
        {
            // Disposing the Event Log must not fail the host shutdown.
        }
    }

    private sealed class SafeLogger(ILogger? inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            try
            {
                return inner?.BeginScope(state);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return inner?.IsEnabled(logLevel) ?? false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                inner?.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception)
            {
                // Swallowed on purpose (BI-085).
            }
        }
    }
}
