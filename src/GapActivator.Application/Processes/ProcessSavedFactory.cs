using GapActivator.Domain.Processes;

namespace GapActivator.Application.Processes;

internal static class ProcessSavedFactory
{
    /// <summary>Names of the other processes that launch the same executable, ordered by name (BI-010).</summary>
    public static async Task<ProcessSaved> CreateAsync(
        IProcessRepository processes, Process saved, CancellationToken ct)
    {
        IReadOnlyList<Process> sharing = await processes.ListByExecutableAsync(saved.Launch.Executable, saved.Id, ct);
        string[] names = [.. sharing.Select(p => p.Name.Value).Order(StringComparer.OrdinalIgnoreCase)];
        return new ProcessSaved(saved.Id, names);
    }
}
