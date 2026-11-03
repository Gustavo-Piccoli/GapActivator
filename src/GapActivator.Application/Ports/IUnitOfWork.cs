using GapActivator.Domain.Common;

namespace GapActivator.Application.Ports;

public interface IUnitOfWork
{
    /// <summary>Events of every aggregate loaded or added in this scope, then cleared.</summary>
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();

    Task CommitAsync(CancellationToken ct);
}
