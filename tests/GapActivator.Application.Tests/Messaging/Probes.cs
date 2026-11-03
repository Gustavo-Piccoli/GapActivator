using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Tests.Messaging;

public sealed record ProbeCommand : ICommand<int>;

public sealed record ProbeInternalCommand : IInternalCommand<int>;

public sealed record ProbeUngatedCommand : ICommand<int>, IUngatedCommand;

public sealed record ProbeQuery : IQuery<string>;

/// <summary>Shared, scripted behavior of the probe handlers, plus an ordered log of what happened.</summary>
public sealed class ProbeState
{
    private readonly List<string> _order = [];
    private readonly Lock _sync = new();
    private int _inFlight;
    private int _handled;
    private int _maxInFlight;

    public Func<CancellationToken, Task> OnHandle { get; set; } = _ => Task.CompletedTask;

    public List<Func<CancellationToken, Task>> Actions { get; } = [];

    public List<IDomainEvent> Published { get; } = [];

    public int Handled => Volatile.Read(ref _handled);

    public int? HistoryBeforeCommit { get; set; }

    public int? HistoryAfterCommit { get; set; }

    public bool? GateHeldDuringEvent { get; set; }

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public string[] Order
    {
        get
        {
            lock (_sync)
            {
                return [.. _order];
            }
        }
    }

    public void Note(string what)
    {
        lock (_sync)
        {
            _order.Add(what);
        }
    }

    public Task NoteAsync(string what)
    {
        Note(what);
        return Task.CompletedTask;
    }

    internal async Task TrackInFlightAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _handled);
        int now = Interlocked.Increment(ref _inFlight);
        int seen;
        do
        {
            seen = Volatile.Read(ref _maxInFlight);
        }
        while (now > seen && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen);

        try
        {
            await OnHandle(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>Toggles the pause flag so that each run raises one domain event.</summary>
public sealed class ProbeCommandHandler(
    ISettingsRepository settings, AfterCommit afterCommit, ProbeState state, TimeProvider time)
    : ICommandHandler<ProbeCommand, int>
{
    public async Task<int> HandleAsync(ProbeCommand command, CancellationToken ct)
    {
        foreach (Func<CancellationToken, Task> action in state.Actions)
        {
            afterCommit.Enqueue(action);
        }

        await state.TrackInFlightAsync(ct);

        InstallationSettings current = await settings.GetAsync(ct);
        if (current.IsPaused)
        {
            current.Resume(time.GetUtcNow());
        }
        else
        {
            current.Pause(time.GetUtcNow());
        }

        return 42;
    }
}

public sealed class ProbeInternalCommandHandler : ICommandHandler<ProbeInternalCommand, int>
{
    public Task<int> HandleAsync(ProbeInternalCommand command, CancellationToken ct) => Task.FromResult(7);
}

/// <summary>Records one event in the buffer and queues the scripted actions; it never touches the store.</summary>
public sealed class ProbeUngatedCommandHandler(
    EventBuffer buffer, AfterCommit afterCommit, ProbeState state, TimeProvider time)
    : ICommandHandler<ProbeUngatedCommand, int>
{
    public Task<int> HandleAsync(ProbeUngatedCommand command, CancellationToken ct)
    {
        foreach (Func<CancellationToken, Task> action in state.Actions)
        {
            afterCommit.Enqueue(action);
        }

        buffer.Record(new CycleFailed("probe failure", time.GetUtcNow()));
        return Task.FromResult(1);
    }
}

public sealed class ProbeQueryHandler(CallerAccessor accessor) : IQueryHandler<ProbeQuery, string>
{
    public Task<string> HandleAsync(ProbeQuery query, CancellationToken ct) => Task.FromResult(accessor.Current.Name);
}

public sealed class RecordingNotificationHandler(ProbeState state, WriteGate gate) : INotificationHandler
{
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        state.Note("event");
        state.GateHeldDuringEvent = gate.IsHeld;
        lock (state.Published)
        {
            state.Published.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}

public sealed class ThrowingNotificationHandler : INotificationHandler
{
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct) =>
        throw new InvalidOperationException("notification failed");
}

/// <summary>Notes "commit" once the real commit has succeeded.</summary>
public sealed class RecordingUnitOfWork(InMemoryUnitOfWork inner, ProbeState state, InMemoryStore store) : IUnitOfWork
{
    public IReadOnlyList<IDomainEvent> DequeueDomainEvents() => inner.DequeueDomainEvents();

    public async Task CommitAsync(CancellationToken ct)
    {
        state.HistoryBeforeCommit = store.History.Count;
        await inner.CommitAsync(ct);
        state.HistoryAfterCommit = store.History.Count;
        state.Note("commit");
    }
}

public static class ProbeHost
{
    public static ApplicationTestHost Create(ProbeState state, Action<IServiceCollection>? extra = null) =>
        new(services =>
        {
            services.AddSingleton(state);
            services.AddScoped<ICommandHandler<ProbeCommand, int>, ProbeCommandHandler>();
            services.AddScoped<ICommandHandler<ProbeInternalCommand, int>, ProbeInternalCommandHandler>();
            services.AddScoped<ICommandHandler<ProbeUngatedCommand, int>, ProbeUngatedCommandHandler>();
            services.AddScoped<IQueryHandler<ProbeQuery, string>, ProbeQueryHandler>();
            services.AddScoped<INotificationHandler, RecordingNotificationHandler>();
            services.AddScoped<IUnitOfWork>(sp => new RecordingUnitOfWork(
                sp.GetRequiredService<InMemoryUnitOfWork>(), sp.GetRequiredService<ProbeState>(),
                sp.GetRequiredService<InMemoryStore>()));
            extra?.Invoke(services);
        });
}
