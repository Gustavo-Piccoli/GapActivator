namespace GapActivator.Domain.Common;

/// <summary>A fact that happened in the domain.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
