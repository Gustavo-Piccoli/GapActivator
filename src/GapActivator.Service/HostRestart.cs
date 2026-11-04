using Microsoft.Extensions.Hosting;

namespace GapActivator.Service;

/// <summary>
/// Stops the host for a restart: sets the restart exit code and asks the host to stop, so the service's recovery
/// actions start it again. Only the first call does anything. While a deferral from <see cref="Defer"/> is open,
/// <see cref="Begin"/> only marks the restart; the last deferral to end runs it, so a caller can hold the stop back until
/// something else (the pipe response) is done.
/// </summary>
public sealed class HostRestart(IHostApplicationLifetime lifetime, ExitCodeSink exitCode)
{
    private readonly Lock _sync = new();
    private int _deferrals;
    private bool _pending;
    private bool _begun;

    /// <summary>Holds a restart back until the returned object is disposed. Disposing more than once is harmless.</summary>
    public IDisposable Defer()
    {
        lock (_sync)
        {
            _deferrals++;
        }

        return new Deferral(this);
    }

    public void Begin()
    {
        lock (_sync)
        {
            if (_begun)
            {
                return;
            }

            if (_deferrals > 0)
            {
                _pending = true;
                return;
            }

            _begun = true;
        }

        Run();
    }

    private void End()
    {
        lock (_sync)
        {
            _deferrals--;
            if (_deferrals > 0 || !_pending || _begun)
            {
                return;
            }

            _pending = false;
            _begun = true;
        }

        Run();
    }

    private void Run()
    {
        exitCode.Set(ServiceExitCodes.Restart);
        lifetime.StopApplication();
    }

    private sealed class Deferral(HostRestart owner) : IDisposable
    {
        private HostRestart? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End();
    }
}
