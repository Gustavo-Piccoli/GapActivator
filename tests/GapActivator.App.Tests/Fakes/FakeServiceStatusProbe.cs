using GapActivator.App.Shell;

namespace GapActivator.App.Tests.Fakes;

internal sealed class FakeServiceStatusProbe : IServiceStatusProbe
{
    private int _queries;

    public ServiceInstallState State { get; set; } = ServiceInstallState.Unknown;

    public int Queries => Volatile.Read(ref _queries);

    public ServiceInstallState Query()
    {
        Interlocked.Increment(ref _queries);
        return State;
    }
}
