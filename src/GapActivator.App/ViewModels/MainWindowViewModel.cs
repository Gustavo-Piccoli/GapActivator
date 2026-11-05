using GapActivator.App.Localization;
using GapActivator.App.Services;

namespace GapActivator.App.ViewModels;

/// <summary>
/// The main window: the navigation rail, the banner above the page and the rail footer. Later tasks add one constructor
/// parameter and one property per page, a nav entry and a data template in MainWindow.axaml.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly INavigator _navigator;
    private NavItemViewModel _selectedNavItem;
    private AppPage _currentPage;

    public MainWindowViewModel(Localizer l, INavigator navigator, ConnectionBannerViewModel banner, Permissions permissions)
        : base(l)
    {
        _navigator = navigator;
        Banner = banner;
        Permissions = permissions;
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
        };
    }

    public ConnectionBannerViewModel Banner { get; }

    public Permissions Permissions { get; }

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

    private void Show(AppPage page)
    {
        CurrentPage = page;
        SelectedNavItem = NavItems.First(i => i.Page == page);
    }
}
