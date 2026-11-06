using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GapActivator.App.Composition;
using GapActivator.App.Services;
using GapActivator.App.Shell;
using GapActivator.App.Updates;
using GapActivator.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.App;

public sealed class App : Avalonia.Application
{
    private static IServiceProvider? s_services;

    /// <summary>Hands the composed services to the app; Avalonia creates the instance through its parameterless constructor.</summary>
    internal static void UseServices(IServiceProvider services) => s_services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            var services = s_services ?? throw new InvalidOperationException("Services were not composed.");
            var uiLogger = services.GetRequiredService<ILogger<App>>();
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppLog.UnhandledUiException(uiLogger, e.Exception);
                e.Handled = true;
            };

            services.GetRequiredService<ThemeApplier>().Start();

            // Every view model exists before the gateway starts, so none misses the first connection change.
            _ = services.GetRequiredService<MainWindowViewModel>();
            services.GetRequiredService<IServiceGateway>().Start();

            // The live state follows the gateway from here on; it refreshes after every connect, and once now if already connected.
            services.GetRequiredService<LiveState>().Start();

            // The daily release check starts last, once the view models that listen to it exist; it is quiet unless a release is newer.
            services.GetRequiredService<UpdateChecker>().StartBackground();

            // The shell window is created here, on the UI thread, and disposed by AppShutdown on the UI thread.
            var shell = services.GetRequiredService<ShellWindow>();
            var windows = services.GetRequiredService<IMainWindowService>();
            shell.ActivationRequested += (_, _) => windows.Show();
            shell.SessionEnding += (_, _) => services.GetRequiredService<IAppLifetime>().Exit();

            // The tray follows the connection and the live state, so it starts after both; the presenter listens for the service's notifications.
            services.GetRequiredService<TrayCoordinator>().Start();
            services.GetRequiredService<NotificationPresenter>().Start();

            if (!services.GetRequiredService<StartupOptions>().Minimized)
            {
                windows.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
