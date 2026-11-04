using GapActivator.Application.Cycle;
using GapActivator.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service;

/// <summary>
/// Prepares the data root and the database, then runs the scheduler loop until the host stops. The stopping token
/// ends it quietly. Any other failure sets the fatal exit code and is rethrown, so the host stops and the service's
/// recovery actions start it again. Plan 4 awaits <see cref="SchedulerLoop.Reconciled"/> before it opens the pipe.
/// </summary>
internal sealed partial class GapActivatorWorker(
    IServiceScopeFactory scopes,
    SchedulerLoop loop,
    ExitCodeSink exitCode,
    ILogger<GapActivatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Returns to the host at once, so the start reported to the Service Control Manager is not held up.
        await Task.Yield();

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogStartupFailed(ex);
            exitCode.Set(ServiceExitCodes.Fatal);
            throw;
        }

        try
        {
            await loop.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: end quietly.
        }
        catch (Exception)
        {
            // The scheduler loop has already logged its failure at Critical, so it is not logged again here.
            exitCode.Set(ServiceExitCodes.Fatal);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Preparing the data root and the database failed; GapActivator stops.")]
    private partial void LogStartupFailed(Exception exception);
}
