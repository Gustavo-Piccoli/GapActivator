using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;

namespace GapActivator.Domain.Runs;

public sealed record RunLate(RunId RunId, ProcessId ProcessId, DateTimeOffset ScheduledFor, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record RunSkipped(RunId RunId, ProcessId ProcessId, DateTimeOffset ScheduledFor, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record RunStarted(RunId RunId, ProcessId ProcessId, int Pid, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record RunCompleted(
    RunId RunId,
    ProcessId ProcessId,
    RunOutcome Outcome,
    int? ExitCode,
    bool Killed,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ServiceProcessCrashed(RunId RunId, ProcessId ProcessId, int? ExitCode, DateTimeOffset OccurredAt)
    : IDomainEvent;
