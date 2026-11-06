using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

internal sealed class FakeAppLifetime : IAppLifetime
{
    public int Exits { get; private set; }

    public void Exit() => Exits++;
}
