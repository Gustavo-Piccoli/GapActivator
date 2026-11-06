using Avalonia;
using Avalonia.Controls;
using GapActivator.App.Settings;
using GapActivator.App.Views;

namespace GapActivator.App.Services;

/// <summary>Creates the main window lazily, restores its saved bounds, and turns its close button into "hide to tray".</summary>
/// <remarks>Position is stored in physical pixels (it is compared with the screens' working areas), size in device-independent pixels.</remarks>
public sealed class MainWindowService(Func<MainWindow> factory, IAppSettingsService settings, Func<IReadOnlyList<PixelRect>> workingAreas)
    : IMainWindowService
{
    private MainWindow? _window;

    /// <summary>The window, once it has been created.</summary>
    public MainWindow? Window => _window;

    public void Show()
    {
        var window = _window ??= Create();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();

        // Brings the window to the front even when another app holds the foreground.
        window.Topmost = true;
        window.Topmost = false;
    }

    /// <summary>
    /// Saves where the window is now, when it is on screen. A Quit from the tray or a Windows session end does not go through the
    /// close button, so without this a move since the last hide would be lost. Call it on the UI thread.
    /// </summary>
    public void SaveBoundsIfVisible()
    {
        if (_window is { IsVisible: true } window)
        {
            SaveBounds(window);
        }
    }

    private MainWindow Create()
    {
        var window = factory();
        window.Closing += OnClosing;
        window.MinWidth = WindowPlacement.MinWidth;
        window.MinHeight = WindowPlacement.MinHeight;
        _window = window;

        WindowBounds? bounds = WindowPlacement.Choose(settings.Current.Window, workingAreas());
        if (bounds is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Width = WindowPlacement.DefaultWidth;
            window.Height = WindowPlacement.DefaultHeight;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(bounds.X, bounds.Y);
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            if (bounds.Maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }

        return window;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason != WindowCloseReason.WindowClosing || sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        SaveBounds(window);
        window.Hide();
    }

    private void SaveBounds(Window window)
    {
        WindowBounds? previous = settings.Current.Window;
        var maximized = window.WindowState == WindowState.Maximized;

        // A maximized or minimized window reports the wrong rectangle: keep the last normal one.
        WindowBounds? next = window.WindowState == WindowState.Normal
            ? new WindowBounds(
                window.Position.X,
                window.Position.Y,
                (int)Math.Round(window.Bounds.Width),
                (int)Math.Round(window.Bounds.Height),
                false)
            : previous is null ? null : previous with { Maximized = maximized };

        if (next is not null)
        {
            settings.Update(s => s with { Window = next });
        }
    }
}
