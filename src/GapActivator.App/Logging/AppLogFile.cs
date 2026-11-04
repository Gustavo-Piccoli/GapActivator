using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Logging;

/// <summary>Best-effort daily log files (<c>app-yyyyMMdd.log</c>) with retention. Never throws.</summary>
public sealed partial class AppLogFile(string directory, TimeProvider time)
{
    public const int RetentionDays = 14;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly object _gate = new();
    private DateOnly? _lastRetentionDay;

    public void Append(DateTimeOffset at, LogLevel level, string category, string message, Exception? error)
    {
        var text = new StringBuilder()
            .Append(at.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(Code(level)).Append("] ")
            .Append(category).Append(": ").Append(message)
            .AppendLine();
        if (error is not null)
        {
            text.AppendLine(error.ToString());
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (_lastRetentionDay != Today())
                {
                    ApplyRetention();
                }

                var name = $"app-{at.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log";
                File.AppendAllText(Path.Combine(directory, name), text.ToString(), Utf8NoBom);
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
            }
        }
    }

    private static string Code(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "TRC",
    };

    private static bool IsFileSystemFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;

    [GeneratedRegex(@"^app-(\d{8})\.log$")]
    private static partial Regex LogFileName();

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private void ApplyRetention()
    {
        var today = Today();
        _lastRetentionDay = today;
        var cutoff = today.AddDays(-RetentionDays);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var match = LogFileName().Match(Path.GetFileName(file));
                if (match.Success
                    && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && date < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (IsFileSystemFailure(ex))
                    {
                        // One undeletable file must not stop the rest of the cleanup.
                    }
                }
            }
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            // A failing sweep must never drop the line being written.
        }
    }
}
