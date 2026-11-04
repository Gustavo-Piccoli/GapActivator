using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Cycle;

public sealed record PurgeHistory(int HistoryRetentionDays) : IInternalCommand<int>;

/// <summary>Purges completed runs and history entries older than the retention period. Returns the number of runs purged.</summary>
internal sealed partial class PurgeHistoryHandler(
    IHistoryRetention retention,
    TimeProvider time,
    ILogger<PurgeHistoryHandler> logger) : ICommandHandler<PurgeHistory, int>
{
    public async Task<int> HandleAsync(PurgeHistory command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset cutoff = time.GetUtcNow() - TimeSpan.FromDays(command.HistoryRetentionDays);
        int purged = await retention.PurgeBeforeAsync(cutoff, ct);
        if (purged > 0)
        {
            LogPurged(purged, cutoff);
        }

        return purged;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Count} runs that ended before {Cutoff}.")]
    private partial void LogPurged(int count, DateTimeOffset cutoff);
}
