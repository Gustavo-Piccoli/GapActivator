using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

internal sealed class FakeServiceStarter : IServiceStarter
{
    public ServiceStartOutcome Outcome { get; set; } = new(ServiceStartResult.Started, 0);

    public int Calls { get; private set; }

    public Task<ServiceStartOutcome> StartAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Outcome);
    }
}
