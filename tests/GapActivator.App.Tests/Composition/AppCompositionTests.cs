using System.Globalization;
using GapActivator.App.Composition;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Shell;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using GapActivator.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Tests.Composition;

[Collection("ShellWin32")]
public sealed class AppCompositionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task Build_RegistersTheCultureApplier_SoAComposerCanReplaceIt()
    {
        using var localAppData = new TempDirectory();
        await using ServiceProvider provider = AppComposition.Build(new AppPaths(localAppData.Path), new StartupOptions(true));

        provider.GetRequiredService<ICultureApplier>().ShouldBeOfType<ProcessCultureApplier>();
    }

    // Nothing is awaited until the provider is disposed: the shell window belongs to the thread that created it, and the shutdown
    // step that destroys it must run on that thread.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Compose_StartLikeTheApp_ThenShutDown_ResolvesEverythingAndEndsCleanly()
    {
        using var localAppData = new TempDirectory();
        var tray = new FakeTrayIcon();
        var shutdownLog = new ListLogger<AppShutdown>();
        var options = new GapActivatorClientOptions
        {
            PipeName = "GapActivator.Tests.Composition." + Guid.NewGuid().ToString("N"),
            ConnectTimeout = TimeSpan.FromMilliseconds(200),
            ClientVersion = AppVersion.Display,
        };
        ServiceProvider provider = AppComposition.Build(
            new AppPaths(localAppData.Path),
            new StartupOptions(true),
            services =>
            {
                // Only what touches the machine: the notification-area icon, the service control manager, the network, the process
                // culture and the pipe (a name nothing listens on, so the real service is never reached). The shell window stays real:
                // it is a hidden window that this thread creates and the shutdown destroys.
                services.Replace(ServiceDescriptor.Singleton<ITrayIcon>(tray));
                services.Replace(ServiceDescriptor.Singleton<IServiceStatusProbe>(new FakeServiceStatusProbe { State = ServiceInstallState.NotInstalled }));
                services.Replace(ServiceDescriptor.Singleton<IUiDispatcher>(new ImmediateUiDispatcher()));
                services.Replace(ServiceDescriptor.Singleton<IReleaseSource>(new FakeReleaseSource()));
                services.Replace(ServiceDescriptor.Singleton<ICultureApplier>(new NoCultureApplier()));
                services.Replace(ServiceDescriptor.Singleton(options));
                services.Replace(ServiceDescriptor.Singleton<ILogger<AppShutdown>>(shutdownLog));
            });

        try
        {
            // Everything App.axaml.cs resolves, and every page.
            provider.GetRequiredService<MainWindowViewModel>().ShouldNotBeNull();
            provider.GetRequiredService<DashboardViewModel>().ShouldNotBeNull();
            provider.GetRequiredService<ProcessesViewModel>().ShouldNotBeNull();
            provider.GetRequiredService<HistoryViewModel>().ShouldNotBeNull();
            provider.GetRequiredService<SettingsViewModel>().ShouldNotBeNull();
            provider.GetRequiredService<AboutViewModel>().ShouldNotBeNull();
            provider.GetRequiredService<Func<ProcessEditorViewModel>>()().Dispose();
            provider.GetRequiredService<Func<ScheduleEditorViewModel>>()().Dispose();
            provider.GetRequiredService<Func<RunDetailViewModel>>()().Dispose();
            provider.GetRequiredService<ThemeApplier>().ShouldNotBeNull();
            provider.GetRequiredService<IServiceGateway>().ShouldNotBeNull();
            provider.GetRequiredService<LiveState>().ShouldNotBeNull();
            provider.GetRequiredService<IUpdateChecker>().ShouldNotBeNull();
            provider.GetRequiredService<UpdateChecker>().ShouldNotBeNull();
            provider.GetRequiredService<IMainWindowService>().ShouldNotBeNull();
            provider.GetRequiredService<IAppLifetime>().ShouldBeOfType<AppShutdown>();
            provider.GetRequiredService<TrayCoordinator>().ShouldNotBeNull();
            provider.GetRequiredService<NotificationPresenter>().ShouldNotBeNull();
            provider.GetRequiredService<ILogger<App>>().ShouldNotBeNull();

            // The launch sequence itself, as the app runs it (minimized: no window).
            App.StartServices(provider);

            var shell = provider.GetRequiredService<ShellWindow>();
            var http = provider.GetRequiredService<HttpClient>();
            shell.Handle.ShouldNotBe(0);
            tray.States.ShouldNotBeEmpty();

            // The shutdown steps, once.
            provider.GetRequiredService<IAppLifetime>().Exit();

            shutdownLog.Levels.ShouldNotContain(LogLevel.Error);
            tray.Disposed.ShouldBeTrue();
            shell.Handle.ShouldBe(0);
            Should.Throw<ObjectDisposedException>(() => http.Timeout = TimeSpan.FromSeconds(5));
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Compose_MainWindowFactory_BuildsTheWindowWithItsViewModel()
    {
        using var localAppData = new TempDirectory();
        await using ServiceProvider provider = AppComposition.Build(
            new AppPaths(localAppData.Path),
            new StartupOptions(true),
            services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITrayIcon>(new FakeTrayIcon()));
                services.Replace(ServiceDescriptor.Singleton<IServiceStatusProbe>(new FakeServiceStatusProbe()));
                services.Replace(ServiceDescriptor.Singleton<IUiDispatcher>(new ImmediateUiDispatcher()));
                services.Replace(ServiceDescriptor.Singleton<IReleaseSource>(new FakeReleaseSource()));
                services.Replace(ServiceDescriptor.Singleton<ICultureApplier>(new NoCultureApplier()));
            });

        (bool hasViewModel, bool shown) = await fixture.Session.Dispatch(
            () =>
            {
                MainWindow window = provider.GetRequiredService<Func<MainWindow>>()();
                window.Show();
                var result = (window.DataContext is MainWindowViewModel, window.IsVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        hasViewModel.ShouldBeTrue();
        shown.ShouldBeTrue();
    }

    private sealed class NoCultureApplier : ICultureApplier
    {
        public void Apply(CultureInfo culture)
        {
        }
    }
}
