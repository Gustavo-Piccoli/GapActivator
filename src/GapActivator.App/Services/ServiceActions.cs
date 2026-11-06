using GapActivator.App.Localization;

namespace GapActivator.App.Services;

/// <summary>Actions on the service itself, shared by the settings page and the tray menu.</summary>
public sealed class ServiceActions(Localizer l, IServiceGateway gateway, IDialogService dialogs)
{
    /// <summary>Asks first (active runs are stopped), then restarts the service. Returns false when the user said no.</summary>
    public async Task<bool> RestartServiceAsync()
    {
        var confirmed = await dialogs.ConfirmAsync(l.RestartServiceTitle, l.RestartServiceConfirm, l.RestartButton).ConfigureAwait(true);
        if (!confirmed)
        {
            return false;
        }

        await gateway.RestartServiceAsync().ConfigureAwait(true);
        return true;
    }
}
