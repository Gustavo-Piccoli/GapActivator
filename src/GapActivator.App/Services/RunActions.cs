using GapActivator.App.Localization;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

/// <summary>Stopping a run: cancel it, or, for a service-kind process, disable the process (with the user's confirmation).</summary>
public sealed class RunActions(Localizer l, IServiceGateway gateway, IDialogService dialogs)
{
    /// <summary>Asks first for a service-kind process (stopping disables it), then stops. A "no" sends nothing.</summary>
    public async Task CancelOrStopAsync(Guid runId, Guid processId, string processName, ProcessKind kind)
    {
        if (kind == ProcessKind.Service)
        {
            var confirmed = await dialogs.ConfirmAsync(l.StopServiceTitle, l.StopServiceConfirm(processName), l.StopService).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
        }

        await StopAsync(runId, processId, kind).ConfigureAwait(true);
    }

    /// <summary>Stops without asking: cancels a run to completion, or disables a service-kind process and cancels its run.</summary>
    public Task StopAsync(Guid runId, Guid processId, ProcessKind kind) =>
        kind == ProcessKind.Service
            ? gateway.DisableProcessAsync(processId, true)
            : gateway.CancelRunAsync(runId);
}
