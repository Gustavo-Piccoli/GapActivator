using Microsoft.Extensions.Hosting;

namespace GapActivator.Service.Tests.Support;

internal sealed class RecordingLifetime : IHostApplicationLifetime
{
    private int _stopCount;

    public int StopCount => Volatile.Read(ref _stopCount);

    public Action? OnStop { get; set; }

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
        Interlocked.Increment(ref _stopCount);
        OnStop?.Invoke();
    }
}
