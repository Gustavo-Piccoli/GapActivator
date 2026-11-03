using Microsoft.Extensions.Logging;

namespace GapActivator.Testing;

public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>Collects everything written through <see cref="ILogger{TCategoryName}"/> in an <see cref="ApplicationTestHost"/>.</summary>
public sealed class LogRecorder
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return [.. _entries];
            }
        }
    }

    internal void Add(LogEntry entry)
    {
        lock (_sync)
        {
            _entries.Add(entry);
        }
    }
}

public sealed class RecordingLogger<T>(LogRecorder recorder) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        recorder.Add(new LogEntry(logLevel, typeof(T).Name, formatter(state, exception), exception));
    }
}
