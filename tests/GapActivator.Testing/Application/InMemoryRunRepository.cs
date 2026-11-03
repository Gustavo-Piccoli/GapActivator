using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Testing;

public sealed class InMemoryRunRepository(InMemoryStore store, InMemoryUnitOfWork unitOfWork) : IRunRepository
{
    public Task<Run?> GetByIdAsync(RunId id, CancellationToken ct)
    {
        Run? run = store.Runs.GetValueOrDefault(id);
        if (run is not null)
        {
            unitOfWork.Track(run);
        }

        return Task.FromResult(run);
    }

    public Task<Run?> GetActiveForProcessAsync(ProcessId processId, CancellationToken ct)
    {
        Run? run = store.Runs.Values.FirstOrDefault(r => r.ProcessId == processId && r.State != RunState.Completed);
        if (run is not null)
        {
            unitOfWork.Track(run);
        }

        return Task.FromResult(run);
    }

    public Task<IReadOnlyList<Run>> ListActiveAsync(CancellationToken ct)
    {
        Run[] active = [.. store.Runs.Values.Where(r => r.State != RunState.Completed)];
        foreach (Run run in active)
        {
            unitOfWork.Track(run);
        }

        return Task.FromResult<IReadOnlyList<Run>>(active);
    }

    public void Add(Run run)
    {
        unitOfWork.Track(run);
        unitOfWork.Stage(() => store.Runs[run.Id] = run);
    }
}
