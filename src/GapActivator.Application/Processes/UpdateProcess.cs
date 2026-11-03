using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Processes;

public sealed record UpdateProcess(ProcessId Id, ProcessDefinition Definition) : ICommand<ProcessSaved>;

internal sealed class UpdateProcessHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ProcessDefinitionMapper mapper,
    TimeProvider time) : ICommandHandler<UpdateProcess, ProcessSaved>
{
    public async Task<ProcessSaved> HandleAsync(UpdateProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcessDefinition definition = command.Definition;

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw new CommandRejectedException(RejectionCodes.NotFound, $"Process {command.Id} was not found.");

        if (definition.Kind != process.Kind && await runs.GetActiveForProcessAsync(process.Id, ct) is not null)
        {
            throw new CommandRejectedException(
                RejectionCodes.HasActiveRun, "The kind of a process cannot change while it has an active run.");
        }

        // BI-001
        ProcessName name = ProcessName.Create(definition.Name);
        if (await processes.NameExistsAsync(name, process.Id, ct))
        {
            throw new CommandRejectedException(RejectionCodes.NameTaken, $"A process named '{name.Value}' already exists.");
        }

        process.Update(
            name,
            definition.Description,
            definition.Kind,
            mapper.MapLaunch(definition, process),
            ProcessDefinitionMapper.MapLimits(definition),
            mapper.MapSchedules(definition, process),
            time.GetUtcNow());

        return await ProcessSavedFactory.CreateAsync(processes, process, ct);
    }
}
