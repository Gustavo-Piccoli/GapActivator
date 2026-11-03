using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Events;

public enum HistoryEntryKind
{
    RunLate,
    RunSkipped,
    RunStarted,
    RunStopRequested,
    RunCompleted,
    ServiceProcessCrashed,
    ProcessBlocked,
    ProcessUnblocked,
    InstallationPaused,
    InstallationResumed,
    CycleFailed,
    GapActivatorStarted,
    GapActivatorRestarted,
}

public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    HistoryEntryKind Kind,
    ProcessId? ProcessId,
    RunId? RunId,
    RunOutcome? Outcome,
    int? ExitCode,
    string? Message);
