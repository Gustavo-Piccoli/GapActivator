using CommunityToolkit.Mvvm.ComponentModel;

namespace GapActivator.App.Services;

public enum AppPage
{
    Dashboard,
    Processes,
    History,
    Settings,
    About,
}

/// <summary>A request to show a page, optionally focused on one process or run.</summary>
public sealed record NavigationRequest(AppPage Page, Guid? Id);

public interface INavigator
{
    AppPage Current { get; }

    event EventHandler<NavigationRequest>? Requested;

    void Navigate(AppPage page);

    void OpenProcess(Guid processId);

    void OpenRun(Guid runId);
}

/// <summary>Lets any view model ask the main window to show a page; <see cref="Current"/> is set before <see cref="Requested"/> is raised.</summary>
public sealed partial class Navigator : ObservableObject, INavigator
{
    public event EventHandler<NavigationRequest>? Requested;

    [ObservableProperty]
    public partial AppPage Current { get; private set; }

    public void Navigate(AppPage page) => Go(new NavigationRequest(page, null));

    public void OpenProcess(Guid processId) => Go(new NavigationRequest(AppPage.Processes, processId));

    public void OpenRun(Guid runId) => Go(new NavigationRequest(AppPage.History, runId));

    private void Go(NavigationRequest request)
    {
        Current = request.Page;
        Requested?.Invoke(this, request);
    }
}
