using GapActivator.Domain.Common;

namespace GapActivator.Domain.Settings;

public sealed record InstallationPaused(DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record InstallationResumed(DateTimeOffset OccurredAt) : IDomainEvent;
