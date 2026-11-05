using Avalonia.Controls;
using GapActivator.App.Localization;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;

namespace GapActivator.App.Services;

/// <summary>Shows a modal dialog over the main window when it is visible, otherwise centered on screen.</summary>
public sealed class AvaloniaDialogService(Localizer localizer, Func<Window?> owner) : IDialogService
{
    public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary, string cancel)
    {
        var completion = new TaskCompletionSource<DialogChoice>();
        var window = new DialogWindow { DataContext = new DialogViewModel(title, message, primary, secondary, cancel) };
        window.Closed += (_, _) => completion.TrySetResult(window.Result);

        if (owner() is { IsVisible: true } parent)
        {
            _ = window.ShowDialog(parent);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Topmost = true;
            window.Show();
            window.Activate();
        }

        return completion.Task;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirm) =>
        await AskAsync(title, message, confirm, null, localizer.Cancel).ConfigureAwait(true) == DialogChoice.Primary;
}
