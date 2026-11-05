using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GapActivator.App.ViewModels;

namespace GapActivator.App.Views;

public sealed partial class ProcessEditorView : UserControl
{
    private ProcessEditorViewModel? _viewModel;

    public ProcessEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow(DataContext as ProcessEditorViewModel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Follow(DataContext as ProcessEditorViewModel);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        Follow(null);
        base.OnUnloaded(e);
    }

    // A refused schedule is reported at the bottom of a long form: bring the line in the Schedules card into view.
    private void Follow(ProcessEditorViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessEditorViewModel.ScheduleErrorText) && _viewModel?.ScheduleErrorText is not null)
        {
            Dispatcher.UIThread.Post(() => this.FindControl<Control>("ScheduleErrorLine")?.BringIntoView());
        }
    }
}
