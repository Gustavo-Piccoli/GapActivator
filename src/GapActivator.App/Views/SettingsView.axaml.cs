using Avalonia.Controls;
using GapActivator.App.ViewModels;

namespace GapActivator.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Coming to the page reads the service values again, unless the user has unsaved edits: those stay until Save.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                _ = viewModel.OnPageShownAsync();
            }
        };
    }
}
