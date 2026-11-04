using GapActivator.Application.Messaging;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Queries;

public sealed record GetSettings : IQuery<SettingsView>;

internal sealed class GetSettingsHandler(ISettingsRepository settings) : IQueryHandler<GetSettings, SettingsView>
{
    public async Task<SettingsView> HandleAsync(GetSettings query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        InstallationSettings current = await settings.GetAsync(ct);
        EmailAlertSettings email = current.Email;

        // The password never leaves the application: only whether one is stored.
        return new SettingsView(
            current.CycleInterval,
            current.FailureLimit,
            current.DefaultTimeLimit,
            current.DefaultGracePeriod,
            current.LogRetentionDays,
            current.HistoryRetentionDays,
            current.LogLevel,
            current.CheckForUpdates,
            current.IsPaused,
            new EmailView(
                email.Enabled,
                email.Host,
                email.Port,
                email.Tls,
                email.Username,
                email.Password is not null,
                email.Sender,
                [.. email.Recipients],
                [.. email.Alerts]));
    }
}
