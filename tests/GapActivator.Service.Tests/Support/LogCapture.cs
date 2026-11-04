using Microsoft.Extensions.Logging;

namespace GapActivator.Service.Tests.Support;

internal sealed record CapturedLog(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>A logger provider that keeps every entry the host's filters let through.</summary>
internal sealed class LogCapture : ILoggerProvider
{
    private readonly List<CapturedLog> _entries = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_sync)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLog entry)
    {
        lock (_sync)
        {
            _entries.Add(entry);
        }
    }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            owner.Add(new CapturedLog(logLevel, category, formatter(state, exception), exception));
        }
    }
}
