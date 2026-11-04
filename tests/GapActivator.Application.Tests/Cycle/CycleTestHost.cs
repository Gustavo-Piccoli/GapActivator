using GapActivator.Application.Cycle;
using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Tests.Cycle;

/// <summary>Shared arrangement of the cycle tests.</summary>
internal static class CycleTestHost
{
    public static ApplicationTestHost Create(Action<IServiceCollection>? configure = null) =>
        RunTestHost.Create(services =>
        {
            services.AddSingleton<CapturingNotificationHandler>();
            services.AddSingleton<INotificationHandler>(sp => sp.GetRequiredService<CapturingNotificationHandler>());
            services.AddSingleton<QueryDispatcher>();
            services.AddSingleton<ScriptedQueryDispatcher>(sp => new ScriptedQueryDispatcher(sp.GetRequiredService<QueryDispatcher>()));
            services.AddSingleton<IQueryDispatcher>(sp => sp.GetRequiredService<ScriptedQueryDispatcher>());
            configure?.Invoke(services);
        });

    public static CycleRunner Cycle(this ApplicationTestHost host) => host.Services.GetRequiredService<CycleRunner>();

    public static SchedulerLoop Loop(this ApplicationTestHost host) => host.Services.GetRequiredService<SchedulerLoop>();

    public static ScriptedQueryDispatcher Queries(this ApplicationTestHost host) =>
        host.Services.GetRequiredService<ScriptedQueryDispatcher>();

    public static CapturingNotificationHandler Notifications(this ApplicationTestHost host) =>
        host.Services.GetRequiredService<CapturingNotificationHandler>();

    /// <summary>A process with a one-time schedule due at the given instant.</summary>
    public static Process AddScheduledProcess(
        this ApplicationTestHost host, DateTimeOffset dueAt, ProcessKind kind = ProcessKind.RunToCompletion)
    {
        Process process = new ProcessBuilder()
            .Named($"Job {host.Store.Processes.Count}")
            .OfKind(kind)
            .WithExecutable(RunTestHost.Executable)
            .WithSchedule(OneTimeSchedule.Create(dueAt))
            .Build();
        host.Store.Processes[process.Id] = process;
        return process;
    }

    public static void ChangeCycleInterval(this ApplicationTestHost host, TimeSpan interval)
    {
        InstallationSettings settings = host.Store.Settings;
        settings.Change(
            interval,
            settings.FailureLimit,
            settings.DefaultTimeLimit,
            settings.DefaultGracePeriod,
            settings.LogRetentionDays,
            settings.HistoryRetentionDays,
            settings.LogLevel,
            settings.CheckForUpdates);
    }

    public static Task EvaluateAsync(this ApplicationTestHost host, Process process) =>
        host.Run<EvaluateProcess, Unit>(new EvaluateProcess(process.Id));

    public static int CycleFailedEntries(this ApplicationTestHost host)
    {
        lock (host.Store.Sync)
        {
            return host.Store.History.Count(h => h.Kind == HistoryEntryKind.CycleFailed);
        }
    }
}

/// <summary>Wraps the real query dispatcher so a test can make the next queries fail.</summary>
internal sealed class ScriptedQueryDispatcher(IQueryDispatcher inner) : IQueryDispatcher
{
    private int _failures;
    private Exception _error = new InvalidOperationException("Simulated query failure.");

    public void FailNext(int count, string message = "Simulated query failure.")
    {
        _error = new InvalidOperationException(message);
        Volatile.Write(ref _failures, count);
    }

    public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, Caller caller, CancellationToken ct)
        where TQuery : IQuery<TResult>
    {
        if (Volatile.Read(ref _failures) > 0)
        {
            Interlocked.Decrement(ref _failures);
            throw _error;
        }

        return inner.QueryAsync<TQuery, TResult>(query, caller, ct);
    }
}

internal sealed class CapturingNotificationHandler : INotificationHandler
{
    private readonly List<IDomainEvent> _events = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<IDomainEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        lock (_sync)
        {
            _events.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}
