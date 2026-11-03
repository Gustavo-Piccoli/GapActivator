using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;

namespace GapActivator.Testing;

/// <summary>Tracks the aggregates of one scope and applies staged changes to the store on commit.</summary>
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

        foreach (Action change in _staged)
        {
            change();
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
