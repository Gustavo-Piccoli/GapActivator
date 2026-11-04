using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GapActivator.Service;

/// <summary>
/// The Windows Service lifetime with the service's exit code and a stop wait hint long enough for the graceful
/// shutdown (addition a): the Service Control Manager waits <see cref="ServiceTimeouts.Shutdown"/> instead of its default.
/// </summary>
internal sealed class GapActivatorServiceLifetime(
    IHostEnvironment environment,
    IHostApplicationLifetime applicationLifetime,
    ILoggerFactory loggerFactory,
    IOptions<HostOptions> hostOptions,
    IOptions<WindowsServiceLifetimeOptions> serviceOptions)
    : WindowsServiceLifetime(environment, applicationLifetime, loggerFactory, hostOptions, serviceOptions)
{
    protected override void OnStop()
    {
        ExitCode = Environment.ExitCode;
        try
        {
            RequestAdditionalTime((int)ServiceTimeouts.Shutdown.TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
            // The service is not in a pending state (for example, the host is stopping on its own); no hint is needed.
        }

        base.OnStop();

        // A code set while the host was stopping still reaches the Service Control Manager.
        ExitCode = Environment.ExitCode;
    }
}
