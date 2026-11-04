using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Logging;

/// <summary>
/// Writes the service log, one file per local day. The live file is opened without <see cref="FileShare.Delete"/>, so
/// retention cannot delete it while it is in use. Logging never throws into the caller (a failed write is dropped).
/// </summary>
[ProviderAlias("GapActivatorFile")]
internal sealed class DailyFileLoggerProvider(GapActivatorPaths paths, LogLevelSwitch level, TimeProvider time) : ILoggerProvider
{
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _writerDay;
    private bool _disposed;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseWriter();
        }
    }

    private static string Abbreviate(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= level.Minimum;

    private void Write(LogLevel logLevel, string category, string message, Exception? exception)
    {
        try
        {
            DateTimeOffset now = time.GetLocalNow();
            var text = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Append(" [").Append(Abbreviate(logLevel)).Append("] ")
                .Append(category).Append(": ").Append(message);
            if (exception is not null)
            {
                foreach (string line in exception.ToString().Split(["\r\n", "\n"], StringSplitOptions.None))
                {
                    text.Append("\r\n    ").Append(line);
                }
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                DateOnly day = DateOnly.FromDateTime(now.DateTime);
                if (_writer is null || day != _writerDay)
                {
                    CloseWriter();
                    paths.EnsureDirectories();
                    var stream = new FileStream(paths.ServiceLogFile(day), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                    _writerDay = day;
                }

                _writer.WriteLine(text.ToString());
            }
        }
        catch (Exception e)
        {
            // Best-effort fallback: the file cannot be written, so drop the writer (it is reopened on the next line).
            Debug.WriteLine($"GapActivator file log write failed: {e.Message}");
            try
            {
                lock (_gate)
                {
                    CloseWriter();
                }
            }
            catch (Exception)
            {
                // Nothing more can be done.
            }
        }
    }

    private void CloseWriter()
    {
        StreamWriter? writer = _writer;
        _writer = null;
        try
        {
            writer?.Dispose();
        }
        catch (Exception)
        {
            // A writer that fails to close is abandoned.
        }
    }

    private sealed class FileLogger(DailyFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => owner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                owner.Write(logLevel, category, formatter(state, exception), exception);
            }
            catch (Exception)
            {
                // A throwing formatter must not reach the caller either.
            }
        }
    }
}
