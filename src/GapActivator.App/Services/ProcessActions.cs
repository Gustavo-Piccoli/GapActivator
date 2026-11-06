using GapActivator.App.Localization;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

public enum DeleteResult
{
    Deleted,
    Declined,
    StillRunning,
}

/// <summary>What the Processes page does to a process, with the questions the user must answer first. Rejections propagate to the caller.</summary>
public sealed class ProcessActions(Localizer l, IServiceGateway gateway, IDialogService dialogs, RunActions runs, TimeProvider time)
{
    public static TimeSpan DeletePollInterval { get; } = TimeSpan.FromMilliseconds(500);

    public static TimeSpan DeleteWaitLimit { get; } = TimeSpan.FromSeconds(60);

    /// <summary>The new run's id after a plain Run now; null after a restart or when the user said no.</summary>
    public async Task<Guid?> RunNowAsync(ProcessSummaryDto process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (process.ActiveRun is null)
        {
            try
            {
                return await gateway.RunNowAsync(process.Id).ConfigureAwait(true);
            }
            catch (GapActivatorRequestException ex) when (ServiceErrors.Is(ex, IpcErrorCodes.RunAlreadyActive))
            {
                // The list was stale: the run started in the meantime, so the question below applies.
            }
        }

        await AskRestartAsync(process).ConfigureAwait(true);
        return null;
    }

    public Task EnableAsync(Guid id) => gateway.EnableProcessAsync(id);

    public async Task DisableAsync(ProcessSummaryDto process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (process.ActiveRun is not { } run)
        {
            await gateway.DisableProcessAsync(process.Id, false).ConfigureAwait(true);
            return;
        }

        if (process.Kind == ProcessKind.Service)
        {
            // CancelOrStopAsync asks, then disables with the run cancelled: nothing more to send here.
            await runs.CancelOrStopAsync(run.RunId, process.Id, process.Name, process.Kind).ConfigureAwait(true);
            return;
        }

        DialogChoice choice = await dialogs.AskAsync(
            l.DisableTitle(process.Name),
            l.DisableActiveMessage(process.Name),
            l.DisableAndCancel,
            l.DisableAndLetFinish,
            l.Cancel).ConfigureAwait(true);
        if (choice != DialogChoice.Cancel)
        {
            await gateway.DisableProcessAsync(process.Id, choice == DialogChoice.Primary).ConfigureAwait(true);
        }
    }

    public Task UnblockAsync(Guid id, bool runNow) => gateway.UnblockProcessAsync(id, runNow);

    /// <param name="process">The process to delete.</param>
    /// <param name="stopping">Called once the user has confirmed and the wait for the run to stop begins, so the page can show progress.</param>
    public async Task<DeleteResult> DeleteAsync(ProcessSummaryDto process, Action? stopping = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (process.ActiveRun is not { } run)
        {
            if (!await dialogs.ConfirmAsync(l.DeleteTitle, l.DeleteConfirm(process.Name), l.Delete).ConfigureAwait(true))
            {
                return DeleteResult.Declined;
            }

            await gateway.DeleteProcessAsync(process.Id).ConfigureAwait(true);
            return DeleteResult.Deleted;
        }

        DialogChoice choice = await dialogs.AskAsync(
            l.DeleteTitle,
            l.DeleteActiveMessage(process.Name),
            l.CancelAndDelete,
            null,
            l.Cancel).ConfigureAwait(true);
        if (choice != DialogChoice.Primary)
        {
            return DeleteResult.Declined;
        }

        // The user has answered once; stopping a service-kind process must not ask again.
        stopping?.Invoke();
        await StopRunAsync(run.RunId, process).ConfigureAwait(true);

        // One bound covers everything below: the polls, a run that started meanwhile, and a delete that lost the race to one.
        var started = time.GetTimestamp();
        var stopped = run.RunId;
        while (true)
        {
            ProcessDetailsDto details = await gateway.GetProcessAsync(process.Id).ConfigureAwait(true);
            if (details.Summary.ActiveRun is not { } active)
            {
                try
                {
                    await gateway.DeleteProcessAsync(process.Id).ConfigureAwait(true);
                    return DeleteResult.Deleted;
                }
                catch (GapActivatorRequestException ex) when (ServiceErrors.Is(ex, IpcErrorCodes.HasActiveRun))
                {
                    // A run started between the read and the delete: read again and stop that one too.
                }
            }
            else if (active.RunId != stopped)
            {
                // A schedule started a new run while the first was stopping.
                stopped = active.RunId;
                await StopRunAsync(active.RunId, process).ConfigureAwait(true);
            }

            if (time.GetElapsedTime(started) >= DeleteWaitLimit)
            {
                return DeleteResult.StillRunning;
            }

            await Task.Delay(DeletePollInterval, time).ConfigureAwait(true);
        }
    }

    // A run that ended before the cancel arrived is what the caller wanted, so "not active" is not an error here.
    private async Task StopRunAsync(Guid runId, ProcessSummaryDto process)
    {
        try
        {
            await runs.StopAsync(runId, process.Id, process.Kind).ConfigureAwait(true);
        }
        catch (GapActivatorRequestException ex) when (ServiceErrors.Is(ex, IpcErrorCodes.RunNotActive))
        {
            // Already over.
        }
    }

    private async Task AskRestartAsync(ProcessSummaryDto process)
    {
        var service = process.Kind == ProcessKind.Service;
        DialogChoice choice = await dialogs.AskAsync(
            l.RunWhileRunningTitle,
            service ? l.RestartServiceKindMessage : l.RunWhileRunningMessage,
            service ? l.RestartButton : l.Yes,
            null,
            l.No).ConfigureAwait(true);
        if (choice == DialogChoice.Primary)
        {
            await gateway.RestartRunAsync(process.Id).ConfigureAwait(true);
        }
    }
}
