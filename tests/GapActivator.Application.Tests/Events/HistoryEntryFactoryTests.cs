using GapActivator.Application.Events;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Tests.Events;

public class HistoryEntryFactoryTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProcessId Process = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly RunId Run = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly Guid EntryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static TheoryData<IDomainEvent, HistoryEntry> KnownEvents => new()
    {
        { new RunLate(Run, Process, At.AddMinutes(-5), At), Expected(HistoryEntryKind.RunLate, Process, Run) },
        { new RunSkipped(Run, Process, At.AddMinutes(-5), At), Expected(HistoryEntryKind.RunSkipped, Process, Run) },
        { new RunStarted(Run, Process, 1234, At), Expected(HistoryEntryKind.RunStarted, Process, Run) },
        {
            new RunStopRequested(Run, Process, StopReason.UserCancel, At),
            Expected(HistoryEntryKind.RunStopRequested, Process, Run, message: "UserCancel")
        },
        {
            new RunCompleted(Run, Process, RunOutcome.Failed, 3, false, At),
            Expected(HistoryEntryKind.RunCompleted, Process, Run, RunOutcome.Failed, 3)
        },
        {
            new RunCompleted(Run, Process, RunOutcome.Cancelled, null, true, At),
            Expected(HistoryEntryKind.RunCompleted, Process, Run, RunOutcome.Cancelled)
        },
        {
            new ServiceProcessCrashed(Run, Process, 9, At),
            Expected(HistoryEntryKind.ServiceProcessCrashed, Process, Run, exitCode: 9)
        },
        {
            new ProcessBlocked(Process, 3, At),
            Expected(HistoryEntryKind.ProcessBlocked, Process, message: "3 consecutive failures")
        },
        { new ProcessUnblocked(Process, At), Expected(HistoryEntryKind.ProcessUnblocked, Process) },
        { new InstallationPaused(At), Expected(HistoryEntryKind.InstallationPaused) },
        { new InstallationResumed(At), Expected(HistoryEntryKind.InstallationResumed) },
        { new CycleFailed("database offline", At), Expected(HistoryEntryKind.CycleFailed, message: "database offline") },
        {
            new GapActivatorStarted(2, 1, At),
            Expected(HistoryEntryKind.GapActivatorStarted, message: "2 re-adopted, 1 abandoned")
        },
        { new GapActivatorRestarted(At), Expected(HistoryEntryKind.GapActivatorRestarted) },
    };

    [Theory]
    [MemberData(nameof(KnownEvents))]
    public void HistoryEntryFactory_MapsEveryKnownEvent(IDomainEvent domainEvent, HistoryEntry expected)
    {
        HistoryEntry? entry = HistoryEntryFactory.From(domainEvent, () => EntryId);

        entry.ShouldBe(expected);
    }

    [Fact]
    public void From_UnknownEvent_ReturnsNull()
    {
        HistoryEntryFactory.From(new UnknownEvent(At), () => EntryId).ShouldBeNull();
    }

    [Fact]
    public void From_EveryHistoryEntryKind_IsProducedByAKnownEvent()
    {
        HistoryEntryKind[] covered = [.. KnownEvents
            .Select(row => HistoryEntryFactory.From(row.Data.Item1, () => EntryId)!.Kind)
            .Distinct()];

        covered.Order().ShouldBe(Enum.GetValues<HistoryEntryKind>().Order());
    }

    private static HistoryEntry Expected(
        HistoryEntryKind kind,
        ProcessId? processId = null,
        RunId? runId = null,
        RunOutcome? outcome = null,
        int? exitCode = null,
        string? message = null) =>
        new(EntryId, At, kind, processId, runId, outcome, exitCode, message);

    private sealed record UnknownEvent(DateTimeOffset OccurredAt) : IDomainEvent;
}
