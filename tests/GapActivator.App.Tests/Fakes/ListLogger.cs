using Microsoft.Extensions.Logging;

namespace GapActivator.App.Tests.Fakes;

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<LogLevel> _levels = [];

    public IReadOnlyList<LogLevel> Levels
    {
        get
        {
            lock (_gate)
            {
                return [.. _levels];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _levels.Add(logLevel);
        }
    }
}
