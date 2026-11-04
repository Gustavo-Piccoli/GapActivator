using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Runs;

/// <summary>
/// Scoped: begins a run inside a command handler and hands it to the <see cref="RunSupervisor"/> once the command has
/// committed. The launch request carries unprotected secrets, so neither it nor its environment values are logged.
/// </summary>
public sealed class RunStarter(
    IRunRepository runs,
    ISecretProtector secrets,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    TimeProvider time)
{
    public Run Begin(
        Process process,
        InstallationSettings settings,
        RunTrigger trigger,
        DateTimeOffset? scheduledFor,
        bool isLate)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(settings);

        Run run = Run.Begin(process.Id, process.Name, process.Kind, trigger, scheduledFor, isLate, time.GetUtcNow());
        var ticket = new RunStartTicket(
            run.Id,
            process.Id,
            CreateRequest(run.Id, process.Launch),
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

    private LaunchRequest CreateRequest(RunId runId, LaunchOptions launch) =>
        new(
            runId,
            launch.Executable.Value,
            launch.Arguments,
            launch.EffectiveWorkingDirectory,
            [.. launch.Environment.Items.Select(ToKeyValue)]);

    private KeyValuePair<string, string> ToKeyValue(EnvironmentVariable variable) =>
        new(variable.Name, variable.SecretValue is { } secret ? secrets.Unprotect(secret) : variable.PlainValue!);
}
