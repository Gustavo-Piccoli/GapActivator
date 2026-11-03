namespace GapActivator.Application.Messaging;

/// <summary>Lets one gated command at a time run between loading its data and committing it.</summary>
public sealed class WriteGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>True while a gated command holds the gate.</summary>
    internal bool IsHeld => _semaphore.CurrentCount == 0;

    internal Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    internal void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
