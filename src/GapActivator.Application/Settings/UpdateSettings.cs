using GapActivator.Application.Messaging;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Settings;

public sealed record UpdateSettings(SettingsInput Settings) : ICommand<Unit>;

/// <summary>The pause flag is not part of the input and stays as it is. A new cycle interval applies from the next cycle.</summary>
internal sealed class UpdateSettingsHandler(
    ISettingsRepository settings,
    EmailSettingsMapper email) : ICommandHandler<UpdateSettings, Unit>
{
    public async Task<Unit> HandleAsync(UpdateSettings command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        SettingsInput input = command.Settings;

        InstallationSettings current = await settings.GetAsync(ct);
        EmailAlertSettings mapped = await email.MapAsync(input.Email, null, ct);

        current.Change(
            input.CycleInterval,
            input.FailureLimit,
            input.DefaultTimeLimit,
            input.DefaultGracePeriod,
            input.LogRetentionDays,
            input.HistoryRetentionDays,
            input.LogLevel,
            input.CheckForUpdates);
        current.ChangeEmail(mapped);

        return Unit.Value;
    }
}
