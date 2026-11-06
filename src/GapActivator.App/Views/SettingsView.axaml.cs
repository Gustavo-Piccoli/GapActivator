using Avalonia.Controls;
using GapActivator.App.ViewModels;

namespace GapActivator.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Coming to the page reads the service values again, so it never shows what an earlier visit left behind.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is SettingsViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
