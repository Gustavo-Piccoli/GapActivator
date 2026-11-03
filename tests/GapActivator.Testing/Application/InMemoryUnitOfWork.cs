using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;

namespace GapActivator.Testing;

/// <summary>
/// Tracks the aggregates of one scope and applies staged changes to the store on commit. A failed commit discards
/// staged adds, removes and history entries, but in-place mutations of loaded aggregates remain visible to later
/// scopes (no cloning). Tests must not assert "state unchanged after a failed commit" through this fake.
/// </summary>
public sealed class InMemoryUnitOfWork(InMemoryStore store) : IUnitOfWork
{
    private readonly List<Action> _staged = [];
    private readonly List<object> _tracked = [];
    private readonly HashSet<object> _trackedSet = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = new List<IDomainEvent>();
        foreach (object aggregate in _tracked)
        {
            switch (aggregate)
            {
                case Domain.Processes.Process process:
                    events.AddRange(process.DomainEvents);
                    process.ClearDomainEvents();
                    break;
                case Domain.Runs.Run run:
                    events.AddRange(run.DomainEvents);
                    run.ClearDomainEvents();
                    break;
                case InstallationSettings settings:
                    events.AddRange(settings.DomainEvents);
                    settings.ClearDomainEvents();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown tracked aggregate type {aggregate.GetType().Name}.");
            }
        }

        return events;
    }

    public Task CommitAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Exception? failure = store.TakeCommitFailure();
        if (failure is not null)
        {
            _staged.Clear();
            throw failure;
        }

        lock (store.Sync)
        {
            foreach (Action change in _staged)
            {
                change();
            }
        }

        _staged.Clear();
        return Task.CompletedTask;
    }

    internal void Track(object aggregate)
    {
        if (_trackedSet.Add(aggregate))
        {
            _tracked.Add(aggregate);
        }
    }

    internal void Stage(Action change) => _staged.Add(change);
}
