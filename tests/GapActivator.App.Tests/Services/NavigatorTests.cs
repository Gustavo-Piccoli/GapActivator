using GapActivator.App.Services;

namespace GapActivator.App.Tests.Services;

public class NavigatorTests
{
    [Fact]
    public void Navigator_OpenRun_SetsHistoryAndRaisesRequestedWithTheId()
    {
        var navigator = new Navigator();
        var runId = Guid.NewGuid();
        NavigationRequest? request = null;
        AppPage? currentWhenRaised = null;
        navigator.Requested += (_, r) =>
        {
            request = r;
            currentWhenRaised = navigator.Current;
        };

        navigator.OpenRun(runId);

        navigator.Current.ShouldBe(AppPage.History);
        currentWhenRaised.ShouldBe(AppPage.History);
        request.ShouldBe(new NavigationRequest(AppPage.History, runId));
    }

    [Fact]
    public void Navigator_OpenProcess_SetsProcessesAndRaisesRequestedWithTheId()
    {
        var navigator = new Navigator();
        var processId = Guid.NewGuid();
        NavigationRequest? request = null;
        navigator.Requested += (_, r) => request = r;

        navigator.OpenProcess(processId);

        navigator.Current.ShouldBe(AppPage.Processes);
        request.ShouldBe(new NavigationRequest(AppPage.Processes, processId));
    }

    [Fact]
    public void Navigator_Navigate_RaisesRequestedWithoutAnId()
    {
        var navigator = new Navigator();
        NavigationRequest? request = null;
        navigator.Requested += (_, r) => request = r;

        navigator.Navigate(AppPage.Settings);

        navigator.Current.ShouldBe(AppPage.Settings);
        request.ShouldBe(new NavigationRequest(AppPage.Settings, null));
    }

    [Fact]
    public void Navigator_StartsOnTheDashboard() => new Navigator().Current.ShouldBe(AppPage.Dashboard);
}
