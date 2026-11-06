using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Services;

public class ServiceActionsTests
{
    private readonly DashboardHarness _h = new();

    [Fact]
    public async Task RestartService_Confirmed_AsksWithTheFixedTextsThenSendsIt()
    {
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        var restarted = await _h.Service.RestartServiceAsync();

        restarted.ShouldBeTrue();
        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Title.ShouldBe(_h.L.RestartServiceTitle);
        asked.Message.ShouldBe(_h.L.RestartServiceConfirm);
        asked.Primary.ShouldBe(_h.L.RestartButton);
        _h.Gateway.Calls.Select(c => c.Method).ShouldBe([nameof(IServiceGateway.RestartServiceAsync)]);
    }

    [Fact]
    public async Task RestartService_Declined_SendsNothing()
    {
        _h.Dialogs.Enqueue(DialogChoice.Cancel);

        var restarted = await _h.Service.RestartServiceAsync();

        restarted.ShouldBeFalse();
        _h.Gateway.Calls.ShouldBeEmpty();
    }
}
