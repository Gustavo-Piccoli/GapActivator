using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

public sealed record ProcessBlocked(ProcessId ProcessId, int FailuresReached, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProcessUnblocked(ProcessId ProcessId, DateTimeOffset OccurredAt) : IDomainEvent;
