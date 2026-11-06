using GapActivator.App.Updates;

namespace GapActivator.App.Tests.Fakes;

/// <summary>A release source that returns a set release (or null, or throws) and counts its calls.</summary>
internal sealed class FakeReleaseSource : IReleaseSource
{
    private int _calls;

    public ReleaseInfo? Release { get; set; }

    public Exception? Failure { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>When true a call never completes by itself: it ends only when the token it received is cancelled.</summary>
    public bool Hang { get; set; }

    /// <summary>Completed when a call has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when a hanging call saw its token cancelled.</summary>
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        Started.TrySetResult();
        if (Hang)
        {
            return WaitForCancellationAsync(cancellationToken);
        }

        return Failure is { } failure ? Task.FromException<ReleaseInfo?>(failure) : Task.FromResult(Release);
    }

    private async Task<ReleaseInfo?> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Cancelled.TrySetResult();
            throw;
        }

        return null;
    }
}
