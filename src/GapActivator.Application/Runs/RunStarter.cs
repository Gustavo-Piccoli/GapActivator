using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Runs;

/// <summary>Scoped: begins a run inside a command handler and hands it to the <see cref="RunSupervisor"/> once the command has committed.</summary>
public sealed class RunStarter(
    IRunRepository runs,
    AfterCommit afterCommit,
    RunSupervisor supervisor)
{
    public Run Begin(
        Process process,
        InstallationSettings settings,
        RunTrigger trigger,
        DateTimeOffset? scheduledFor,
        bool isLate,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(settings);

        Run run = Run.Begin(process.Id, process.Name, process.Kind, trigger, scheduledFor, isLate, now);
        LaunchOptions launch = process.Launch;
        var ticket = new RunStartTicket(
            run.Id,
            process.Id,
            launch.Executable.Value,
            launch.Arguments,
            launch.EffectiveWorkingDirectory,
            launch.Environment,
            process.EffectiveTimeLimit(settings.DefaultTimeLimit),
            process.EffectiveGracePeriod(settings.DefaultGracePeriod));

        runs.Add(run);
        afterCommit.Enqueue(_ =>
        {
            supervisor.Start(ticket);
            return Task.CompletedTask;
        });
        return run;
    }
}
