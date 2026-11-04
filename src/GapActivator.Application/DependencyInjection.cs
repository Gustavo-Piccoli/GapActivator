using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Processes;
using GapActivator.Application.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GapActivator.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application layer. The host or tests may override the <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<WriteGate>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<CallerAccessor>();
        services.AddScoped<AfterCommit>();
        services.AddScoped<EventBuffer>();
        services.AddScoped<EventPublisher>();

        services.AddScoped<ProcessDefinitionMapper>();
        services.AddScoped<ICommandHandler<RegisterProcess, ProcessSaved>, RegisterProcessHandler>();
        services.AddScoped<ICommandHandler<UpdateProcess, ProcessSaved>, UpdateProcessHandler>();
        services.AddScoped<ICommandHandler<DeleteProcess, Unit>, DeleteProcessHandler>();

        services.AddSingleton<RunSupervisor>();
        services.AddScoped<RunStarter>();
        services.AddScoped<RunOutcomeApplier>();
        services.AddScoped<ICommandHandler<RecordRunLaunched, Unit>, RecordRunLaunchedHandler>();
        services.AddScoped<ICommandHandler<RecordRunLaunchFailed, Unit>, RecordRunLaunchFailedHandler>();
        services.AddScoped<ICommandHandler<RecordLaunchAborted, Unit>, RecordLaunchAbortedHandler>();
        services.AddScoped<ICommandHandler<RecordRunExited, Unit>, RecordRunExitedHandler>();
        services.AddScoped<ICommandHandler<StopRun, Unit>, StopRunHandler>();
        services.AddScoped<ICommandHandler<StartPendingRun, Unit>, StartPendingRunHandler>();
        return services;
    }
}
