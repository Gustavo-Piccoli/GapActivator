using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Processes;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Runs;

/// <summary>Shared arrangement of the run tests. Every await that could hang goes through <see cref="Within"/>.</summary>
internal static class RunTestHost
{
    public const string Executable = ProcessDefinitions.SampleExecutable;

    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);

    /// <param name="configure">Applied after the scripted dispatcher is installed, so it can replace any registration.</param>
    public static ApplicationTestHost Create(Action<IServiceCollection>? configure = null) =>
        new(services =>
        {
            ScriptedCommandDispatcher.Install(services);
            configure?.Invoke(services);
        });

    /// <summary>Ends the supervisor's lifetime, as the container does when the host shuts down.</summary>
    public static void DisposeSupervisor(this ApplicationTestHost host) =>
        host.Supervisor().Dispose();

    public static RunSupervisor Supervisor(this ApplicationTestHost host) =>
        host.Services.GetRequiredService<RunSupervisor>();

    public static ScriptedCommandDispatcher Dispatcher(this ApplicationTestHost host) =>
        host.Services.GetRequiredService<ScriptedCommandDispatcher>();

    public static Process AddProcess(
        this ApplicationTestHost host,
        ProcessKind kind = ProcessKind.RunToCompletion,
        TimeSpan? timeLimit = null,
        TimeSpan? gracePeriod = null)
    {
        Process process = new ProcessBuilder()
            .OfKind(kind)
            .WithExecutable(Executable)
            .WithLimits(new ExecutionLimits(timeLimit, gracePeriod))
            .Build();
        host.Store.Processes[process.Id] = process;
        return process;
    }

    /// <summary>Begins a run through the real pipeline; it is tracked by the supervisor when this returns.</summary>
    public static async Task<Run> StartRunAsync(this ApplicationTestHost host, Process process)
    {
        await host.Run<StartPendingRun, Unit>(new StartPendingRun(process.Id));
        return host.ActiveRun(process);
    }

    public static Run ActiveRun(this ApplicationTestHost host, Process process)
    {
        lock (host.Store.Sync)
        {
            return host.Store.Runs.Values.Single(r => r.ProcessId == process.Id && r.State != RunState.Completed);
        }
    }

    /// <summary>A run already in the store, as if the OS process were running.</summary>
    public static Run AddRunningRun(this ApplicationTestHost host, Process process, int pid = 4242)
    {
        DateTimeOffset now = host.Time.GetUtcNow();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, now);
        run.MarkRunning(pid, now, now);
        run.ClearDomainEvents();
        host.Store.Runs[run.Id] = run;
        return run;
    }

    public static Task Within(this Task task) =>
        task.WaitAsync(SafetyTimeout, TestContext.Current.CancellationToken);

    public static Task EndOf(this ApplicationTestHost host, RunId runId) =>
        host.Supervisor().WaitForEndAsync(runId, TestContext.Current.CancellationToken).Within();

    /// <summary>Waits until the given number of timers exists, so the clock move cannot be missed, then moves the clock.</summary>
    public static async Task AdvanceAfterTimerAsync(this ApplicationTestHost host, int timers, TimeSpan by)
    {
        await host.Time.WaitForTimersAsync(timers).Within();
        host.Time.Advance(by);
    }

    /// <summary>
    /// Keeps moving the clock until the task completes, for a flow whose number of timers depends on a harmless race,
    /// such as a retry delay cancelled just before or just after it was created. Yields instead of waiting in real time.
    /// </summary>
    public static async Task PumpClockUntilAsync(this ApplicationTestHost host, Task done, TimeSpan step)
    {
        Task guarded = done.Within();
        while (!guarded.IsCompleted)
        {
            host.Time.Advance(step);
            await Task.Yield();
        }

        await guarded;
    }

    public static int ErrorsLogged(this ApplicationTestHost host, string category) =>
        host.Log.Entries.Count(e => e.Level == LogLevel.Error && e.Category == category);

    public static bool LoggedAt(this ApplicationTestHost host, LogLevel level, string category) =>
        host.Log.Entries.Any(e => e.Level == level && e.Category == category);

    public static int RunCompletedEntries(this ApplicationTestHost host, RunId runId)
    {
        lock (host.Store.Sync)
        {
            return host.Store.History.Count(h =>
                h.Kind == Application.Events.HistoryEntryKind.RunCompleted && h.RunId == runId);
        }
    }
}
