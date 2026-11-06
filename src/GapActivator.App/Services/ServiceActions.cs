using GapActivator.App.Localization;

namespace GapActivator.App.Services;

/// <summary>Actions on the service itself, shared by the settings page and the tray menu.</summary>
public sealed class ServiceActions(Localizer l, IServiceGateway gateway, IDialogService dialogs)
{
    /// <summary>Asks first (active runs are stopped), then restarts the service. Returns false when the user said no.</summary>
    public async Task<bool> RestartServiceAsync()
    {
        if (!await ConfirmRestartAsync().ConfigureAwait(true))
        {
            return false;
        }

        await SendRestartAsync().ConfigureAwait(true);
        return true;
    }

    /// <summary>The question alone, for a caller that must keep it outside its error handling.</summary>
    public Task<bool> ConfirmRestartAsync() =>
        dialogs.ConfirmAsync(l.RestartServiceTitle, l.RestartServiceConfirm, l.RestartButton);

    /// <summary>The restart alone, without asking.</summary>
    public Task SendRestartAsync() => gateway.RestartServiceAsync();
}
