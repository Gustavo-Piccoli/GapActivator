using GapActivator.Application.Cycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service.Ipc;

/// <summary>
/// Runs the pipe server once the scheduler has reconciled. It is registered after the worker, so the host stops it first:
/// the connections close before the scheduler's shutdown begins. A pipe that cannot be created (for example a name that
/// another process already holds) is fatal: it is logged at Critical, the exit code is set and the host stops.
/// </summary>
internal sealed partial class IpcHostedService(
    IpcServer server,
    SchedulerLoop loop,
    ExitCodeSink exitCode,
    ILogger<IpcHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Returns to the host at once, so the start reported to the Service Control Manager is not held up.
        await Task.Yield();

        try
        {
            await server.RunAsync(loop.Reconciled, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: end quietly.
        }
        catch (Exception ex)
        {
            LogServerFailed(ex);
            exitCode.Set(ServiceExitCodes.Fatal);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The pipe server could not start or failed; GapActivator stops.")]
    private partial void LogServerFailed(Exception exception);
}
