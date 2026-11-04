using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
