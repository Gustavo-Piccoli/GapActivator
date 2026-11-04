using GapActivator.Application.Ports;

namespace GapActivator.Service;

/// <summary>
/// The restart port of one scope. <see cref="RestartAsync"/> only marks the request and returns at once; the restart
/// begins when the scope is disposed. The command dispatcher disposes the command scope after it has published the
/// command's events, so the stop begins only then (addition c).
/// </summary>
internal sealed class ScopedServiceControl(HostRestart restart) : IServiceControl, IAsyncDisposable
{
    private volatile bool _requested;

    public Task RestartAsync(CancellationToken ct)
    {
        _requested = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_requested)
        {
            restart.Begin();
        }

        return ValueTask.CompletedTask;
    }
}
