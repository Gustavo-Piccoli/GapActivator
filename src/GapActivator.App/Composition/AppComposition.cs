using System.Globalization;
using Avalonia;
using GapActivator.App.Localization;
using GapActivator.App.Logging;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Shell;
using GapActivator.App.Updates;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using GapActivator.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Composition;

/// <summary>The composition root. Only explicit singleton factories, so trimming and Native AOT are safe.</summary>
public static class AppComposition
{
    public static ServiceProvider Build(AppPaths paths, StartupOptions options, Action<IServiceCollection>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(TimeZoneInfo.Local);
        services.AddSingleton(sp => new Localizer(sp.GetRequiredService<IAppSettingsService>().Current.Language));
        services.AddSingleton<IUiDispatcher>(_ => new AvaloniaUiDispatcher());

        AddLogging(services);
        AddSettings(services);
        AddGateway(services);
        AddShell(services);
        AddUpdates(services);
        AddUi(services);
        AddTray(services);
        AddShutdown(services);

        overrides?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static void AddLogging(IServiceCollection services)
    {
        services.AddSingleton(sp => new AppLogFile(sp.GetRequiredService<AppPaths>().LogsDirectory, sp.GetRequiredService<TimeProvider>()));
        Logger<App>(services);
        Logger<AppSettingsStore>(services);
        Logger<ServiceGateway>(services);
        Logger<LiveState>(services);
        Logger<UpdateChecker>(services);
        Logger<AppShutdown>(services);
    }

    private static void Logger<T>(IServiceCollection services) =>
        services.AddSingleton<ILogger<T>>(sp => new AppLogger<T>(sp.GetRequiredService<AppLogFile>(), sp.GetRequiredService<TimeProvider>()));

    private static void AddSettings(IServiceCollection services)
    {
        services.AddSingleton(sp => new AppSettingsStore(
            sp.GetRequiredService<AppPaths>().SettingsFile,
            sp.GetRequiredService<ILogger<AppSettingsStore>>(),
            CultureInfo.CurrentUICulture));
        services.AddSingleton<IAppSettingsService>(sp =>
        {
            var store = sp.GetRequiredService<AppSettingsStore>();
            return new AppSettingsService(store, store.Load());
        });
        services.AddSingleton<ICultureApplier>(_ => new ProcessCultureApplier());
        services.AddSingleton(sp => new ThemeApplier(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<ICultureApplier>()));
    }

    private static void AddGateway(IServiceCollection services)
    {
        services.AddSingleton(_ => new GapActivatorClientOptions { ClientVersion = AppVersion.Display });
        services.AddSingleton<IServiceStatusProbe>(_ => new ScmServiceStatusProbe(IpcProtocol.ServiceName));

        services.AddSingleton(sp => new ServiceGateway(
            sp.GetRequiredService<GapActivatorClientOptions>(),
            sp.GetRequiredService<IServiceStatusProbe>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ServiceGateway>>()));
        services.AddSingleton<IServiceGateway>(sp => sp.GetRequiredService<ServiceGateway>());
    }

    private static void AddShell(IServiceCollection services)
    {
        services.AddSingleton(_ => new ShellWindow());
        services.AddSingleton<ITrayIcon>(sp => new Win32TrayIcon(
            sp.GetRequiredService<ShellWindow>(),
            TrayIconImages.Colors.ToDictionary(c => c.Key, c => TrayIconImages.Create(c.Value))));
    }

    private static void AddUpdates(IServiceCollection services)
    {
        // One HttpClient for the whole life of the app: the only outbound request is the release check.
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<IReleaseSource>(sp => new GitHubReleaseSource(sp.GetRequiredService<HttpClient>(), AppVersion.Display));
        services.AddSingleton(sp => new UpdateChecker(
            sp.GetRequiredService<IReleaseSource>(),
            ReleaseVersion.TryParse(AppVersion.Display, out var current) ? current : new Version(0, 0, 0),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<UpdateChecker>>()));
        services.AddSingleton<IUpdateChecker>(sp => sp.GetRequiredService<UpdateChecker>());
        services.AddSingleton<IShellLauncher>(_ => new ShellLauncher());
        services.AddSingleton(ServicePaths.Default);
    }

    private static void AddUi(IServiceCollection services)
    {
        services.AddSingleton(_ => new Navigator());
        services.AddSingleton<INavigator>(sp => sp.GetRequiredService<Navigator>());
        services.AddSingleton(sp => new Permissions(sp.GetRequiredService<Localizer>(), sp.GetRequiredService<IServiceGateway>()));
        services.AddSingleton<IServiceStarter>(_ => new ScServiceStarter());
        services.AddSingleton<IDialogService>(sp =>
            new AvaloniaDialogService(sp.GetRequiredService<Localizer>(), () => sp.GetRequiredService<MainWindowService>().Window));

        services.AddSingleton(sp => new ConnectionBannerViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<IServiceStarter>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new LiveState(sp.GetRequiredService<IServiceGateway>(), sp.GetRequiredService<ILogger<LiveState>>()));
        services.AddSingleton(sp => new FailureAcknowledgements(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new TimeDisplay(sp.GetRequiredService<Localizer>(), sp.GetRequiredService<TimeZoneInfo>()));
        services.AddSingleton(sp => new RunActions(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<IDialogService>()));
        services.AddSingleton(sp => new ProcessActions(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<RunActions>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new DashboardViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<LiveState>(),
            sp.GetRequiredService<Permissions>(),
            sp.GetRequiredService<RunActions>(),
            sp.GetRequiredService<FailureAcknowledgements>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<TimeDisplay>()));
        services.AddSingleton<IFilePicker>(sp =>
            new AvaloniaFilePicker(sp.GetRequiredService<Localizer>(), () => sp.GetRequiredService<MainWindowService>().Window));
        services.AddSingleton<Func<ScheduleEditorViewModel>>(sp => () => new ScheduleEditorViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<TimeZoneInfo>(),
            sp.GetRequiredService<TimeDisplay>(),
            sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<Func<ProcessEditorViewModel>>(sp => () => new ProcessEditorViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<Permissions>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<FailureAcknowledgements>(),
            sp.GetRequiredService<TimeDisplay>(),
            sp.GetRequiredService<TimeZoneInfo>(),
            sp.GetRequiredService<Func<ScheduleEditorViewModel>>(),
            sp.GetRequiredService<IFilePicker>()));
        services.AddSingleton(sp => new ProcessesViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<LiveState>(),
            sp.GetRequiredService<Permissions>(),
            sp.GetRequiredService<ProcessActions>(),
            sp.GetRequiredService<RunActions>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<TimeDisplay>(),
            sp.GetRequiredService<Func<ProcessEditorViewModel>>()));
        services.AddSingleton<IClipboardService>(sp =>
            new AvaloniaClipboardService(() => sp.GetRequiredService<MainWindowService>().Window));
        services.AddSingleton<Func<RunDetailViewModel>>(sp => () => new RunDetailViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<TimeDisplay>(),
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(sp => new HistoryViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<LiveState>(),
            sp.GetRequiredService<FailureAcknowledgements>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<TimeDisplay>(),
            sp.GetRequiredService<TimeZoneInfo>(),
            sp.GetRequiredService<Func<RunDetailViewModel>>()));
        services.AddSingleton(sp => new ServiceActions(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<IDialogService>()));
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<Permissions>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ServiceActions>()));
        services.AddSingleton(sp => new AboutViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<IUpdateChecker>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<IShellLauncher>(),
            sp.GetRequiredService<AppPaths>(),
            sp.GetRequiredService<ServicePaths>(),
            sp.GetRequiredService<IUiDispatcher>(),
            AppVersion.Display));
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<ConnectionBannerViewModel>(),
            sp.GetRequiredService<Permissions>(),
            sp.GetRequiredService<DashboardViewModel>(),
            sp.GetRequiredService<ProcessesViewModel>(),
            sp.GetRequiredService<HistoryViewModel>(),
            sp.GetRequiredService<SettingsViewModel>(),
            sp.GetRequiredService<AboutViewModel>(),
            sp.GetRequiredService<IUpdateChecker>(),
            sp.GetRequiredService<IUiDispatcher>()));

        services.AddSingleton<Func<MainWindow>>(sp => () => new MainWindow { DataContext = sp.GetRequiredService<MainWindowViewModel>() });
        services.AddSingleton(sp => new MainWindowService(
            sp.GetRequiredService<Func<MainWindow>>(),
            sp.GetRequiredService<IAppSettingsService>(),
            () => sp.GetRequiredService<MainWindowService>().Window?.Screens.All.Select(s => s.WorkingArea).ToList() ?? []));
        services.AddSingleton<IMainWindowService>(sp => sp.GetRequiredService<MainWindowService>());
    }

    private static void AddTray(IServiceCollection services)
    {
        services.AddSingleton(sp => new TrayCoordinator(
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<LiveState>(),
            sp.GetRequiredService<FailureAcknowledgements>(),
            sp.GetRequiredService<ITrayIcon>(),
            sp.GetRequiredService<IMainWindowService>(),
            sp.GetRequiredService<IAppLifetime>(),
            sp.GetRequiredService<ServiceActions>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(sp => new NotificationPresenter(
            sp.GetRequiredService<IServiceGateway>(),
            sp.GetRequiredService<ITrayIcon>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<Localizer>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IUiDispatcher>()));
    }

    private static void AddShutdown(IServiceCollection services)
    {
        services.AddSingleton<IAppLifetime>(sp => sp.GetRequiredService<AppShutdown>());
        services.AddSingleton(sp =>
        {
            // Resolved at exit time, not now: the steps run on the UI thread, where the shell objects must be disposed.
            T Get<T>()
                where T : notnull => sp.GetRequiredService<T>();

            // The tray icon goes before the shell window that owns its message loop; the gateway goes after the things that listen to it.
            Action[] steps =
            [
                () => Get<TrayCoordinator>().Dispose(),
                () => Get<NotificationPresenter>().Dispose(),
                () => Get<UpdateChecker>().Dispose(),

                // The gateway loop runs off the UI context, so this wait cannot deadlock; it is bounded.
                () => Get<IServiceGateway>().DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)),
                () => Get<ITrayIcon>().Dispose(),
                () => Get<ShellWindow>().Dispose(),
                RequestDesktopShutdown,
            ];
            return new AppShutdown(steps, sp.GetRequiredService<ILogger<AppShutdown>>());
        });
    }

    private static void RequestDesktopShutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }
}
