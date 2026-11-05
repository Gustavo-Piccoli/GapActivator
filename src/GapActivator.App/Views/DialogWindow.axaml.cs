using Avalonia.Controls;
using Avalonia.Interactivity;
using GapActivator.App.Services;

namespace GapActivator.App.Views;

public sealed partial class DialogWindow : Window
{
    public DialogWindow() => InitializeComponent();

    /// <summary>The button the user pressed; Cancel when the window was closed any other way.</summary>
    public DialogChoice Result { get; private set; } = DialogChoice.Cancel;

    private void OnPrimary(object? sender, RoutedEventArgs e) => Choose(DialogChoice.Primary);

    private void OnSecondary(object? sender, RoutedEventArgs e) => Choose(DialogChoice.Secondary);

    private void OnCancel(object? sender, RoutedEventArgs e) => Choose(DialogChoice.Cancel);

    private void Choose(DialogChoice choice)
    {
        Result = choice;
        Close();
    }
}
