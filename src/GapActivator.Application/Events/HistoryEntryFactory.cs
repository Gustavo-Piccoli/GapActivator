using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Events;

/// <summary>Turns domain and application events into history entries.</summary>
public static class HistoryEntryFactory
{
    /// <summary>The entry for the event, or null when the event type has no history entry.</summary>
    public static HistoryEntry? From(IDomainEvent e, Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(newId);

        HistoryEntry Entry(
            HistoryEntryKind kind,
            ProcessId? processId = null,
            RunId? runId = null,
            RunOutcome? outcome = null,
            int? exitCode = null,
            string? message = null) =>
            new(newId(), e.OccurredAt, kind, processId, runId, outcome, exitCode, message);

        return e switch
        {
            RunLate x => Entry(HistoryEntryKind.RunLate, x.ProcessId, x.RunId),
            RunSkipped x => Entry(HistoryEntryKind.RunSkipped, x.ProcessId, x.RunId),
            RunStarted x => Entry(HistoryEntryKind.RunStarted, x.ProcessId, x.RunId),
            RunStopRequested x => Entry(HistoryEntryKind.RunStopRequested, x.ProcessId, x.RunId, message: x.Reason.ToString()),
            RunCompleted x => Entry(HistoryEntryKind.RunCompleted, x.ProcessId, x.RunId, x.Outcome, x.ExitCode),
            ServiceProcessCrashed x => Entry(HistoryEntryKind.ServiceProcessCrashed, x.ProcessId, x.RunId, exitCode: x.ExitCode),
            ProcessBlocked x => Entry(HistoryEntryKind.ProcessBlocked, x.ProcessId, message: $"{x.FailuresReached} consecutive failures"),
            ProcessUnblocked x => Entry(HistoryEntryKind.ProcessUnblocked, x.ProcessId),
            InstallationPaused => Entry(HistoryEntryKind.InstallationPaused),
            InstallationResumed => Entry(HistoryEntryKind.InstallationResumed),
            CycleFailed x => Entry(HistoryEntryKind.CycleFailed, message: x.Error),
            GapActivatorStarted x => Entry(
                HistoryEntryKind.GapActivatorStarted, message: $"{x.ReadoptedRuns} re-adopted, {x.AbandonedRuns} abandoned"),
            GapActivatorRestarted => Entry(HistoryEntryKind.GapActivatorRestarted),
            _ => null,
        };
    }
}
