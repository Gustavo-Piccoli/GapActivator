using GapActivator.Application.Messaging;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Queries;

public sealed record CycleSettings(bool IsPaused, TimeSpan CycleInterval, int LogRetentionDays, int HistoryRetentionDays);

public sealed record GetCycleSettings : IQuery<CycleSettings>;

internal sealed class GetCycleSettingsHandler(ISettingsRepository settings) : IQueryHandler<GetCycleSettings, CycleSettings>
{
    public async Task<CycleSettings> HandleAsync(GetCycleSettings query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        InstallationSettings current = await settings.GetAsync(ct);
        return new CycleSettings(current.IsPaused, current.CycleInterval, current.LogRetentionDays, current.HistoryRetentionDays);
    }
}
