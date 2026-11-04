using GapActivator.Domain.Processes;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// Processes with their schedules. Reads are tracked, so the unit of work saves what the handler changed. Names and
/// executables are compared through the upper-invariant shadow keys, which fold non-ASCII case as the domain does.
/// </summary>
internal sealed class EfProcessRepository(GapActivatorDbContext db) : IProcessRepository
{
    public Task<Process?> GetByIdAsync(ProcessId id, CancellationToken ct) =>
        db.Processes.SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Process>> ListAsync(CancellationToken ct) =>
        await db.Processes.ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Reads the ids only, without materializing a process, so one unreadable row cannot fail it (BI-039).</summary>
    public async Task<IReadOnlyList<ProcessId>> ListIdsAsync(CancellationToken ct) =>
        await db.Processes.Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);

    public Task<bool> NameExistsAsync(ProcessName name, ProcessId? excluding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        string key = name.Value.ToUpperInvariant();
        return Excluding(db.Processes.Where(p => EF.Property<string>(p, ProcessConfiguration.NameKey) == key), excluding)
            .AnyAsync(ct);
    }

    public async Task<IReadOnlyList<Process>> ListByExecutableAsync(
        ExecutablePath executable,
        ProcessId? excluding,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(executable);
        string key = executable.Value.ToUpperInvariant();
        return await Excluding(
                db.Processes.Where(p => EF.Property<string>(p, ProcessConfiguration.ExecutableKey) == key), excluding)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public void Add(Process process) => db.Processes.Add(process);

    public void Remove(Process process) => db.Processes.Remove(process);

    private static IQueryable<Process> Excluding(IQueryable<Process> query, ProcessId? excluding) =>
        excluding is { } id ? query.Where(p => p.Id != id) : query;
}
