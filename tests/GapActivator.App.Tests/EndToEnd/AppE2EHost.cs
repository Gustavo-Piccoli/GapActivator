using System.Globalization;
using GapActivator.App.Composition;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Shell;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;
using GapActivator.Application;
using GapActivator.Application.Cycle;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Application.Queries;
using GapActivator.Application.Settings;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Ipc;
using GapActivator.Service;
using GapActivator.Service.Ipc;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Tests.EndToEnd;

/// <summary>
/// The real GapActivator host, in-process, on a data root and a unique pipe. Started without a <c>dataRoot</c> it creates the
/// root, initializes and seeds it (a 5-second cycle) and deletes it when disposed. Started with one it uses that root as it is
/// and never deletes it: a second host on the root of a stopped first one is how the tests restart the service. The caller
/// resolver is scripted, the exit-code sink is replaced and every log provider is removed, so a test never writes to the
/// machine's Event Log. Disposing it stops the host (30 s bound) and kills and reports a leftover sample child.
/// </summary>
internal sealed class AppE2EHost : IAsyncDisposable
{
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(30);
    private static readonly string[] ChildProcessNames = ["GapActivator.SampleChild"];
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly TempDataRoot? _ownedRoot;
    private readonly IHost _host;
    private bool _stopped;

    private AppE2EHost(TempDataRoot? ownedRoot, string dataRoot, IHost host, string pipeName, TestCallerResolver callers)
    {
        _ownedRoot = ownedRoot;
        DataRoot = dataRoot;
        _host = host;
        PipeName = pipeName;
        Callers = callers;
    }

    public string PipeName { get; }

    public string DataRoot { get; }

    public TestCallerResolver Callers { get; }

    /// <summary>
    /// Starts the host and waits until startup reconciliation has finished (the pipe opens right after). Without
    /// <paramref name="dataRoot"/> the root is new and seeded; <paramref name="pipeName"/> defaults to a unique one.
    /// </summary>
    public static async Task<AppE2EHost> StartAsync(CancellationToken ct, string? pipeName = null, string? dataRoot = null)
    {
        TempDataRoot? owned = dataRoot is null ? new TempDataRoot() : null;
        string root = dataRoot ?? owned!.Path;
        IHost? host = null;
        try
        {
            if (owned is not null)
            {
                await SeedAsync(root, ct);
            }

            HostApplicationBuilder builder = GapActivatorHost.CreateBuilder(
                [$"--{GapActivatorPaths.DataRootKey}={root}", $"--{IpcOptions.PipeNameKey}={pipeName ?? $"GapActivator.Tests.{Guid.NewGuid():N}"}"]);
            string pipe = builder.Configuration[IpcOptions.PipeNameKey]
                ?? throw new InvalidOperationException("The test host has no pipe name.");

            // Every provider is a DI registration, so this switches the Event Log, the file and the console off.
            builder.Logging.ClearProviders();
            builder.Services.Replace(ServiceDescriptor.Singleton(new ExitCodeSink(_ => { })));
            var callers = new TestCallerResolver();
            builder.Services.Replace(ServiceDescriptor.Singleton<ICallerResolver>(callers));

            host = builder.Build();
            using (var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // Bounded on its own: the token cancels a start that honours it, the wait caps one that does not.
                startTimeout.CancelAfter(StartWait);
                await host.StartAsync(startTimeout.Token).WaitAsync(StartWait, ct);
            }

            await host.Services.GetRequiredService<SchedulerLoop>().Reconciled.WaitAsync(ReconcileWait, ct);
            return new AppE2EHost(owned, root, host, pipe, callers);
        }
        catch
        {
            if (host is not null)
            {
                await StopHostAsync(host);
                host.Dispose();
            }

            GapActivatorDatabase.ReleasePooledConnections();
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }

            throw;
        }
    }

    /// <summary>
    /// Stops the host, bounded to 30 seconds, and fails if a sample child is still running afterwards (it is killed first). Safe
    /// to call more than once; disposing the host calls it too, so a host a test stopped by hand is checked as well.
    /// </summary>
    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        LeftoverChildren children = await LeftoverChildren.OpenAsync(_host.Services.GetRequiredService<IQueryDispatcher>(), ChildProcessNames);
        List<int> leftovers;
        try
        {
            await StopHostAsync(_host);
        }
        finally
        {
            leftovers = children.KillAndReport();
        }

        if (leftovers.Count > 0)
        {
            throw new InvalidOperationException(
                $"The host stopped but left children running (killed now): PIDs {string.Join(", ", leftovers)}.");
        }
    }

    /// <summary>
    /// The app as <c>Program</c> composes it, with the pieces that would touch the machine replaced. The settings are saved in
    /// English first, so the texts the tests compare with do not depend on the machine's UI language. Only view models and
    /// services are resolved: nothing creates the shell window or a tray icon. Dispose the provider with
    /// <c>DisposeAsync</c>, before the host.
    /// </summary>
    public ServiceProvider ComposeApp(string localAppData)
    {
        var paths = new AppPaths(localAppData);
        _ = new AppSettingsStore(paths.SettingsFile, new ListLogger<AppSettingsStore>(), English).Save(AppSettings.Defaults(English));

        return AppComposition.Build(
            paths,
            new StartupOptions(true),
            services =>
            {
                services.Replace(ServiceDescriptor.Singleton(new GapActivatorClientOptions
                {
                    PipeName = PipeName,
                    ServiceName = "GapActivator.Tests.NotInstalled",
                    ConnectTimeout = TimeSpan.FromSeconds(2),
                    ClientVersion = AppVersion.Display,
                }));
                services.Replace(ServiceDescriptor.Singleton<IServiceStatusProbe>(new FakeServiceStatusProbe { State = ServiceInstallState.NotInstalled }));
                services.Replace(ServiceDescriptor.Singleton<IUiDispatcher>(new ImmediateUiDispatcher()));
                services.Replace(ServiceDescriptor.Singleton<IDialogService>(new AlwaysPrimaryDialogService()));

                // Never the process culture (other test classes run in parallel) and never the network.
                services.Replace(ServiceDescriptor.Singleton<ICultureApplier>(new NoCultureApplier()));
                services.Replace(ServiceDescriptor.Singleton<IReleaseSource>(new FakeReleaseSource()));
            });
    }

    /// <summary>Stops the host (see <see cref="StopAsync"/>), disposes it and deletes the root it created.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            _host.Dispose();
            GapActivatorDatabase.ReleasePooledConnections();
            if (_ownedRoot is not null)
            {
                await _ownedRoot.DisposeAsync();
            }
        }
    }

    private static async Task StopHostAsync(IHost host)
    {
        using var deadline = new CancellationTokenSource(StopWait);
        await host.StopAsync(deadline.Token);
    }

    /// <summary>Initializes the database and writes the one setting the tests need: a 5-second cycle, e-mail off.</summary>
    private static async Task SeedAsync(string dataRoot, CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new InfrastructureOptions(new GapActivatorPaths(dataRoot)));
        services.AddSingleton<IServiceControl, FakeServiceControl>();
        services.AddSingleton<IAppNotifier, RecordingAppNotifier>();
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        try
        {
            await using (AsyncServiceScope scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(ct);
            }

            InstallationSettings defaults = InstallationSettings.Default();
            EmailAlertSettings email = EmailAlertSettings.Disabled;
            await provider.GetRequiredService<ICommandDispatcher>().SendAsync<UpdateSettings, Unit>(
                new UpdateSettings(new SettingsInput(
                    TimeSpan.FromSeconds(5),
                    defaults.FailureLimit,
                    defaults.DefaultTimeLimit,
                    defaults.DefaultGracePeriod,
                    defaults.LogRetentionDays,
                    defaults.HistoryRetentionDays,
                    defaults.LogLevel,
                    defaults.CheckForUpdates,
                    new EmailInput(
                        false,
                        email.Host,
                        email.Port,
                        email.Tls,
                        email.Username,
                        new SecretChange(SecretChangeKind.Clear, null),
                        email.Sender,
                        email.Recipients,
                        email.Alerts))),
                Caller.System,
                ct);
        }
        finally
        {
            GapActivatorDatabase.ReleasePooledConnections();
        }
    }

    private sealed class NoCultureApplier : ICultureApplier
    {
        public void Apply(CultureInfo culture)
        {
        }
    }
}
