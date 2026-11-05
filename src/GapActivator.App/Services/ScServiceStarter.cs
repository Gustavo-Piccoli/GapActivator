using System.ComponentModel;
using System.Diagnostics;
using GapActivator.Ipc;

namespace GapActivator.App.Services;

/// <summary>Runs <c>sc.exe start</c> elevated through the UAC prompt.</summary>
public sealed class ScServiceStarter : IServiceStarter
{
    private const int ErrorCancelled = 1223;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorWaitTimeout = 258;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Runs off the calling thread: the UAC prompt blocks <c>Process.Start</c> until the user answers.</summary>
    public Task<ServiceStartOutcome> StartAsync(CancellationToken ct) => Task.Run(() => RunAsync(ct), CancellationToken.None);

    private static async Task<ServiceStartOutcome> RunAsync(CancellationToken ct)
    {
        var info = new ProcessStartInfo("sc.exe", "start " + IpcProtocol.ServiceName)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception ex)
        {
            return ex.NativeErrorCode == ErrorCancelled
                ? new ServiceStartOutcome(ServiceStartResult.Declined, ErrorCancelled)
                : new ServiceStartOutcome(ServiceStartResult.Failed, ex.NativeErrorCode);
        }

        if (process is null)
        {
            return new ServiceStartOutcome(ServiceStartResult.Failed, 0);
        }

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new ServiceStartOutcome(ServiceStartResult.Failed, ErrorWaitTimeout);
            }

            return MapExitCode(process.ExitCode);
        }
    }

    /// <summary>0 and 1056 (already running) mean started; any other code is a failure with that code.</summary>
    internal static ServiceStartOutcome MapExitCode(int exitCode) =>
        exitCode is 0 or ErrorServiceAlreadyRunning
            ? new ServiceStartOutcome(ServiceStartResult.Started, 0)
            : new ServiceStartOutcome(ServiceStartResult.Failed, exitCode);
}
