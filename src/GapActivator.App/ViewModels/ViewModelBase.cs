using CommunityToolkit.Mvvm.ComponentModel;
using GapActivator.App.Localization;

namespace GapActivator.App.ViewModels;

public abstract class ViewModelBase(Localizer l) : ObservableObject
{
    public Localizer L { get; } = l;
}
