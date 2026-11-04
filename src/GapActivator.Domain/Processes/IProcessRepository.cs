namespace GapActivator.Domain.Processes;

public interface IProcessRepository
{
    Task<Process?> GetByIdAsync(ProcessId id, CancellationToken ct);

    Task<IReadOnlyList<Process>> ListAsync(CancellationToken ct);

    /// <summary>Reads the ids only, so one process that cannot be loaded does not fail the listing (BI-039).</summary>
    Task<IReadOnlyList<ProcessId>> ListIdsAsync(CancellationToken ct);

    Task<bool> NameExistsAsync(ProcessName name, ProcessId? excluding, CancellationToken ct);

    Task<IReadOnlyList<Process>> ListByExecutableAsync(
        ExecutablePath executable,
        ProcessId? excluding,
        CancellationToken ct);

    void Add(Process process);

    void Remove(Process process);
}
