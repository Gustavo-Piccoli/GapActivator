using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;

namespace GapActivator.Application.Processes;

public sealed record RegisterProcess(ProcessDefinition Definition) : ICommand<ProcessSaved>;

internal sealed class RegisterProcessHandler(
    IProcessRepository processes,
    ProcessDefinitionMapper mapper,
    TimeProvider time) : ICommandHandler<RegisterProcess, ProcessSaved>
{
    public async Task<ProcessSaved> HandleAsync(RegisterProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcessDefinition definition = command.Definition;

        // BI-001
        ProcessName name = ProcessName.Create(definition.Name);
        if (await processes.NameExistsAsync(name, null, ct))
        {
            throw new CommandRejectedException(RejectionCodes.NameTaken, $"A process named '{name.Value}' already exists.");
        }

        Process process = Process.Register(
            name,
            definition.Description,
            definition.Kind,
            mapper.MapLaunch(definition, null),
            ProcessDefinitionMapper.MapLimits(definition),
            mapper.MapSchedules(definition, null),
            time.GetUtcNow());
        processes.Add(process);

        return await ProcessSavedFactory.CreateAsync(processes, process, ct);
    }
}
