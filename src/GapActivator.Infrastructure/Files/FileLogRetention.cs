using GapActivator.Application.Ports;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Files;

/// <summary>
/// Deletes service logs and run output files older than a number of days, by last write time. Files still open without
/// <see cref="FileShare.Delete"/> (the live service log, the output of a running child) cannot be deleted and are skipped.
/// </summary>
internal sealed partial class FileLogRetention(GapActivatorPaths paths, TimeProvider time, ILogger<FileLogRetention> logger)
    : ILogRetention
{
    public Task PurgeOlderThanAsync(int days, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DateTime cutoff = (time.GetUtcNow() - TimeSpan.FromDays(days)).UtcDateTime;
        Purge(paths.LogsDirectory, "gapactivator-*.log", cutoff, ct);
        Purge(paths.RunLogsDirectory, "*.log", cutoff, ct);
        return Task.CompletedTask;
    }

    private void Purge(string directory, string pattern, DateTime cutoff, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogSkipped(e, directory);
            return;
        }

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogSkipped(e, file);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retention skipped {Path}; it is in use or not accessible.")]
    private partial void LogSkipped(Exception exception, string path);
}
