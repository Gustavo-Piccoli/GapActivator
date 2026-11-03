namespace GapActivator.Application.Ports;

public interface IHistoryRetention
{
    /// <summary>Purges completed runs that ended before the cutoff, their entries, and run-less entries before the cutoff.</summary>
    Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct);
}
