using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Logging;

/// <summary>Wraps the Event Log provider so that a missing source or a denied write never reaches the caller.</summary>
[ProviderAlias("EventLog")]
internal sealed class SafeEventLogLoggerProvider(ILoggerProvider inner, EventLogRateLimiter limiter) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        try
        {
            return new SafeLogger(inner.CreateLogger(categoryName), categoryName, limiter);
        }
        catch (Exception)
        {
            return new SafeLogger(null, categoryName, limiter);
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

    private sealed class SafeLogger(ILogger? inner, string category, EventLogRateLimiter limiter) : ILogger
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
                if (inner is null || !IsEnabled(logLevel))
                {
                    return;
                }

                // The key leaves out the exception, so a repeated failure with a new stack is still one entry.
                string text = formatter(state, exception);
                if (limiter.TryAcquire(category, text))
                {
                    inner.Log(logLevel, eventId, state, exception, formatter);
                }
            }
            catch (Exception)
            {
                // Swallowed on purpose (BI-085).
            }
        }
    }
}
