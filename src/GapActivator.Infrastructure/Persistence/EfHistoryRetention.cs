using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// Purges with set-based deletes that bypass the change tracker, in one transaction, so a failure leaves no entry
/// without its run or the reverse.
/// </summary>
internal sealed class EfHistoryRetention(GapActivatorDbContext db) : IHistoryRetention
{
    public async Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        await db.HistoryEntries
            .Where(e => (e.RunId == null && e.OccurredAt < cutoff)
                || (e.RunId != null
                    && db.Runs.Any(r => r.Id == e.RunId && r.State == RunState.Completed && r.EndedAt < cutoff)))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        int purged = await db.Runs
            .Where(r => r.State == RunState.Completed && r.EndedAt < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return purged;
    }
}
