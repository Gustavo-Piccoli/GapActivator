using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Queries;

public sealed record GetDashboard : IQuery<Dashboard>;

internal sealed class GetDashboardHandler(
    IProcessRepository processes,
    IRunRepository runs,
    IRunHistoryReader history,
    ISettingsRepository settings,
    TimeProvider time) : IQueryHandler<GetDashboard, Dashboard>
{
    private const int Limit = 10;
    private const int FailureReadSize = 200;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);

    public async Task<Dashboard> HandleAsync(GetDashboard query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateTimeOffset now = time.GetUtcNow();

        InstallationSettings current = await settings.GetAsync(ct);
        IReadOnlyList<Run> active = await runs.ListActiveAsync(ct);
        IReadOnlyList<Process> all = await processes.ListAsync(ct);

        // The outcomes come from the failure rule, so a new outcome is classified in one place.
        RunOutcome[] failing = [.. Enum.GetValues<RunOutcome>().Where(o => RunOutcomeRules.EffectOf(o) == FailureEffect.Increment)];

        // Bounded approximation: only the newest 200 failed runs are read, then cut to the last 24 hours.
        // With more than 200 failures in that window the dashboard shows the newest ones, which is all it needs.
        RunPage failures = await history.ListRunsAsync(new RunFilter(null, failing, null, null, 1, FailureReadSize), ct);
        DateTimeOffset since = now - FailureWindow;

        UpcomingRun[] upcoming =
        [
            .. all
                .Where(p => p.Lifecycle == ProcessLifecycle.Enabled && p.NextScheduledAt is not null)
                .Select(p => new UpcomingRun(p.Id, p.Name.Value, p.NextScheduledAt!.Value))
                .OrderBy(u => u.At)
                .ThenBy(u => u.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Take(Limit),
        ];

        return new Dashboard(
            current.IsPaused,
            [.. active.OrderBy(r => r.CreatedAt).Select(QueryMapping.ToRow)],
            upcoming,
            [.. failures.Items
                .Where(r => r.EndedAt >= since)
                .OrderByDescending(r => r.EndedAt)
                .Take(Limit)
                .Select(QueryMapping.ToRow)]);
    }
}
