using GapActivator.Domain.Common;

namespace GapActivator.Application.Events;

/// <summary>Reacts to a published event, for example by sending an alert.</summary>
public interface INotificationHandler
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct);
}
