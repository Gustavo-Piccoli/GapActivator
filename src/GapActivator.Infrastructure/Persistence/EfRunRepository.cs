using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>Runs, tracked. A run is active until its state is <see cref="RunState.Completed"/>.</summary>
internal sealed class EfRunRepository(GapActivatorDbContext db) : IRunRepository
{
    public Task<Run?> GetByIdAsync(RunId id, CancellationToken ct) =>
        db.Runs.SingleOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>The unique filtered index allows at most one active run per process.</summary>
    public Task<Run?> GetActiveForProcessAsync(ProcessId processId, CancellationToken ct) =>
        db.Runs.SingleOrDefaultAsync(r => r.ProcessId == processId && r.State != RunState.Completed, ct);

    public async Task<IReadOnlyList<Run>> ListActiveAsync(CancellationToken ct) =>
        await db.Runs.Where(r => r.State != RunState.Completed).ToListAsync(ct).ConfigureAwait(false);

    public void Add(Run run) => db.Runs.Add(run);
}
