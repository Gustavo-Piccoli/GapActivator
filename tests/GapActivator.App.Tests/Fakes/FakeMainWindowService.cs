using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

internal sealed class FakeMainWindowService : IMainWindowService
{
    public int Shown { get; private set; }

    public void Show() => Shown++;
}
