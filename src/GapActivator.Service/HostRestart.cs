using Microsoft.Extensions.Hosting;

namespace GapActivator.Service;

/// <summary>
/// Stops the host for a restart: sets the restart exit code and asks the host to stop, so the service's recovery
/// actions start it again. Only the first call does anything.
/// </summary>
public sealed class HostRestart(IHostApplicationLifetime lifetime, ExitCodeSink exitCode)
{
    private int _begun;

    public void Begin()
    {
        if (Interlocked.Exchange(ref _begun, 1) != 0)
        {
            return;
        }

        exitCode.Set(ServiceExitCodes.Restart);
        lifetime.StopApplication();
    }
}
