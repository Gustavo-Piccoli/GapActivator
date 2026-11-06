using GapActivator.App.Updates;

namespace GapActivator.App.Tests.Fakes;

/// <summary>A release source that returns a set release (or null, or throws) and counts its calls.</summary>
internal sealed class FakeReleaseSource : IReleaseSource
{
    private int _calls;

    public ReleaseInfo? Release { get; set; }

    public Exception? Failure { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Failure is { } failure ? Task.FromException<ReleaseInfo?>(failure) : Task.FromResult(Release);
    }
}
