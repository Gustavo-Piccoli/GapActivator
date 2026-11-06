using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Updates;

namespace GapActivator.App.ViewModels;

/// <summary>
/// The main window: the navigation rail, the banner above the page and the rail footer. Later tasks add one constructor
/// parameter and one property per page, a nav entry and a data template in MainWindow.axaml.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigator _navigator;
    private NavItemViewModel _selectedNavItem;
    private AppPage _currentPage;
    private Version? _availableUpdate;

    public MainWindowViewModel(Localizer l, INavigator navigator, ConnectionBannerViewModel banner, Permissions permissions, DashboardViewModel dashboard, ProcessesViewModel processes, HistoryViewModel history, SettingsViewModel settings, AboutViewModel about, IUpdateChecker updates, IUiDispatcher ui)
        : base(l)
    {
        _navigator = navigator;
        Banner = banner;
        Permissions = permissions;
        Dashboard = dashboard;
        Processes = processes;
        History = history;
        Settings = settings;
        About = about;
        NavItems =
        [
            new NavItemViewModel(AppPage.Dashboard, "", l),
            new NavItemViewModel(AppPage.Processes, "", l),
            new NavItemViewModel(AppPage.History, "", l),
            new NavItemViewModel(AppPage.Settings, "", l),
            new NavItemViewModel(AppPage.About, "", l),
        ];
        _currentPage = navigator.Current;
        _selectedNavItem = NavItems.First(i => i.Page == _currentPage);

        navigator.Requested += (_, request) => Show(request.Page);
        banner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConnectionBannerViewModel.IsConnected) or nameof(ConnectionBannerViewModel.IsDevelopmentMode))
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(FooterText));
                OnPropertyChanged(nameof(FooterGlyph));
            }
        };
        l.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(UpdateNotice));
        };

        // The checker's timer runs on a pool thread, so the notice is written on the UI thread.
        if (updates.Last is { Outcome: UpdateCheckOutcome.UpdateAvailable, LatestVersion: { } known })
        {
            _availableUpdate = known;
        }

        updates.UpdateFound += (_, result) => ui.Post(() => ShowUpdate(result));
    }

    public ConnectionBannerViewModel Banner { get; }

    public Permissions Permissions { get; }

    public DashboardViewModel Dashboard { get; }

    public ProcessesViewModel Processes { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    /// <summary>The rail-footer notice that a newer release exists, or null. Clicking it opens About.</summary>
    public string? UpdateNotice => _availableUpdate is { } version ? L.UpdateAvailableNotice(version.ToString()) : null;

    public bool HasUpdateNotice => _availableUpdate is not null;

    /// <summary>The view model of the current page, or null for a page that is not built yet (the window then shows its title).</summary>
    public ViewModelBase? PageContent => CurrentPage switch
    {
        AppPage.Dashboard => Dashboard,
        AppPage.Processes => Processes,
        AppPage.History => History,
        AppPage.Settings => Settings,
        AppPage.About => About,
        _ => null,
    };

    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    public NavItemViewModel SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedNavItem))
            {
                // A list box deselects while its items change; keep the current page and push it back.
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _selectedNavItem, value) && value.Page != _navigator.Current)
            {
                _navigator.Navigate(value.Page);
            }
        }
    }

    public AppPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageContent));
            }
        }
    }

    public string PageTitle => NavItems.First(i => i.Page == CurrentPage).Label;

    public bool IsConnected => Banner.IsConnected;

    // A development service is connected but is not the installed one, so the footer does not call that "Connected".
    private bool FooterHealthy => IsConnected && !Banner.IsDevelopmentMode;

    public string FooterText => FooterHealthy ? L.FooterConnected : L.FooterNotConnected;

    /// <summary>A check mark when connected to the installed service, a warning sign otherwise: the state never rests on color alone.</summary>
    public string FooterGlyph => FooterHealthy ? "" : "";

    [RelayCommand]
    private void OpenUpdateNotice() => _navigator.Navigate(AppPage.About);

    private void ShowUpdate(UpdateCheckResult result)
    {
        if (result.Outcome != UpdateCheckOutcome.UpdateAvailable || result.LatestVersion is null)
        {
            return;
        }

        _availableUpdate = result.LatestVersion;
        OnPropertyChanged(nameof(UpdateNotice));
        OnPropertyChanged(nameof(HasUpdateNotice));
    }

    private void Show(AppPage page)
    {
        CurrentPage = page;
        SelectedNavItem = NavItems.First(i => i.Page == page);
    }
}
