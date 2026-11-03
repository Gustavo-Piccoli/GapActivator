using GapActivator.Domain.Common;

namespace GapActivator.Application.Events;

public sealed record CycleFailed(string Error, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record GapActivatorStarted(int ReadoptedRuns, int AbandonedRuns, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Restart requested from the app.</summary>
public sealed record GapActivatorRestarted(DateTimeOffset OccurredAt) : IDomainEvent;
