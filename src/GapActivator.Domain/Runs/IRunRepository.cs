using GapActivator.Domain.Processes;

namespace GapActivator.Domain.Runs;

public interface IRunRepository
{
    Task<Run?> GetByIdAsync(RunId id, CancellationToken ct);

    Task<Run?> GetActiveForProcessAsync(ProcessId processId, CancellationToken ct);

    Task<IReadOnlyList<Run>> ListActiveAsync(CancellationToken ct);

    void Add(Run run);
}
