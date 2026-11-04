using GapActivator.Application.Messaging;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Settings;

public sealed record PauseInstallation : ICommand<Unit>;

/// <summary>Pausing stops new runs only; active runs finish normally (Q-1).</summary>
internal sealed class PauseInstallationHandler(
    ISettingsRepository settings,
    TimeProvider time) : ICommandHandler<PauseInstallation, Unit>
{
    public async Task<Unit> HandleAsync(PauseInstallation command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await settings.GetAsync(ct)).Pause(time.GetUtcNow());
        return Unit.Value;
    }
}
