using GapActivator.App.Localization;
using GapActivator.App.Services;

namespace GapActivator.App.ViewModels;

/// <summary>One entry of the navigation rail.</summary>
public sealed class NavItemViewModel : ViewModelBase
{
    public NavItemViewModel(AppPage page, string glyph, Localizer l)
        : base(l)
    {
        Page = page;
        Glyph = glyph;
        l.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public AppPage Page { get; }

    /// <summary>A Segoe Fluent Icons code point.</summary>
    public string Glyph { get; }

    public string Label => Page switch
    {
        AppPage.Dashboard => L.NavDashboard,
        AppPage.Processes => L.NavProcesses,
        AppPage.History => L.NavHistory,
        AppPage.Settings => L.NavSettings,
        _ => L.NavAbout,
    };
}
