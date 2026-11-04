using Microsoft.Extensions.Logging;

namespace GapActivator.App.Logging;

/// <summary>Writes Information and above to the app log file; a logger never throws.</summary>
public sealed class AppLogger<T>(AppLogFile file, TimeProvider time) : ILogger<T>
{
    private static readonly string Category = typeof(T).Name;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and not LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            file.Append(time.GetLocalNow(), logLevel, Category, formatter(state, exception), exception);
        }
#pragma warning disable CA1031 // A logger must never throw, whatever the formatter or the file does.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
