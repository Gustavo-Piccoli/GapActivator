using GapActivator.Domain.Processes;

namespace GapActivator.Testing;

public sealed class InMemoryProcessRepository(InMemoryStore store, InMemoryUnitOfWork unitOfWork) : IProcessRepository
{
    public Task<Process?> GetByIdAsync(ProcessId id, CancellationToken ct)
    {
        if (store.ShouldFailOnLoad(id))
        {
            throw new InvalidOperationException($"Simulated failure while loading process {id}.");
        }

        Process? process = store.Processes.GetValueOrDefault(id);
        if (process is not null)
        {
            unitOfWork.Track(process);
        }

        return Task.FromResult(process);
    }

    public Task<IReadOnlyList<Process>> ListAsync(CancellationToken ct)
    {
        Process[] all = [.. store.Processes.Values];
        foreach (Process process in all)
        {
            unitOfWork.Track(process);
        }

        return Task.FromResult<IReadOnlyList<Process>>(all);
    }

    public Task<bool> NameExistsAsync(ProcessName name, ProcessId? excluding, CancellationToken ct) =>
        Task.FromResult(store.Processes.Values.Any(p => p.Id != excluding && p.Name == name));

    public Task<IReadOnlyList<Process>> ListByExecutableAsync(ExecutablePath executable, ProcessId? excluding, CancellationToken ct)
    {
        Process[] matches = [.. store.Processes.Values.Where(p => p.Id != excluding && p.Launch.Executable == executable)];
        foreach (Process process in matches)
        {
            unitOfWork.Track(process);
        }

        return Task.FromResult<IReadOnlyList<Process>>(matches);
    }

    public void Add(Process process)
    {
        unitOfWork.Track(process);
        unitOfWork.Stage(() => store.Processes[process.Id] = process);
    }

    public void Remove(Process process)
    {
        unitOfWork.Track(process);
        unitOfWork.Stage(() => store.Processes.Remove(process.Id));
    }
}
