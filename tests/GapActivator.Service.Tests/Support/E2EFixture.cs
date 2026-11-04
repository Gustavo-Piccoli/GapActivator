using System.Diagnostics;
using GapActivator.Application;
using GapActivator.Application.Cycle;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Application.Queries;
using GapActivator.Application.Settings;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Ipc;
using GapActivator.Service.Ipc;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace GapActivator.Service.Tests.Support;

/// <summary>
/// A seeded <see cref="TempDataRoot"/> and the real GapActivator host started on it, in-process. The seed is written
/// before the host starts, through a separate <see cref="E2ESeeding"/> provider: a 5-second cycle, e-mail disabled, and
/// three processes (<see cref="ScheduledName"/>, <see cref="LongName"/>, <see cref="StubbornName"/>). Commands and
/// queries go through the host's dispatchers as <see cref="Caller.System"/>. Disposing it stops the host, bounded, and
/// fails if a child is still running afterwards.
/// </summary>
internal sealed class E2EFixture : IAsyncDisposable
{
    /// <summary>The sample child with <c>1</c> (about one second of work), due once three seconds after the seed.</summary>
    public const string ScheduledName = "Sample scheduled";

    /// <summary>The sample child with <c>60</c>, unscheduled; it stops cleanly when cancelled.</summary>
    public const string LongName = "Sample long";

    /// <summary>The test child with <c>ignore-cancel</c>, unscheduled, with a 2-second time limit and a 1-second grace period.</summary>
    public const string StubbornName = "Stubborn";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ChildExitWait = TimeSpan.FromSeconds(10);
    private static readonly string[] ChildProcessNames = ["GapActivator.SampleChild", "GapActivator.TestChild"];

    private readonly TempDataRoot _root;
    private readonly IReadOnlyDictionary<string, ProcessId> _processes;
    private readonly IHost _host;
    private readonly List<GapActivatorClient> _clients = [];
    private bool _stopped;

    private E2EFixture(
        TempDataRoot root,
        IReadOnlyDictionary<string, ProcessId> processes,
        IHost host,
        string pipeName,
        FakeCallerResolver callers)
    {
        _root = root;
        _processes = processes;
        _host = host;
        PipeName = pipeName;
        Callers = callers;
    }

    public string DataRoot => _root.Path;

    /// <summary>The unique pipe the host listens on.</summary>
    public string PipeName { get; }

    /// <summary>The scripted caller resolver the host's pipe server uses: the operator unless <c>Next</c> says otherwise.</summary>
    public FakeCallerResolver Callers { get; }

    /// <summary>
    /// Seeds a new data root and starts the host on it, with <see cref="FakeCallerResolver"/> as the pipe's caller
    /// resolver. <paramref name="configure"/> runs last, so it may replace anything. Unless
    /// <paramref name="waitForReconcile"/> is false, it waits until startup reconciliation has finished.
    /// </summary>
    public static async Task<E2EFixture> StartAsync(
        CancellationToken ct,
        Action<HostApplicationBuilder>? configure = null,
        bool waitForReconcile = true)
    {
        var root = new TempDataRoot();
        IHost? host = null;
        try
        {
            IReadOnlyDictionary<string, ProcessId> processes = await SeedAsync(root.Path, ct);
            HostApplicationBuilder builder = ServiceHostFactory.CreateBuilder(root.Path, []);
            string pipeName = builder.Configuration[IpcOptions.PipeNameKey]
                ?? throw new InvalidOperationException("The test host has no pipe name.");
            var callers = new FakeCallerResolver();
            builder.Services.Replace(ServiceDescriptor.Singleton<ICallerResolver>(callers));
            configure?.Invoke(builder);
            host = builder.Build();
            using (var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // Bounded on its own: the token cancels a start that honours it, the wait caps one that does not.
                startTimeout.CancelAfter(StartWait);
                await host.StartAsync(startTimeout.Token).WaitAsync(StartWait, ct);
            }

            if (waitForReconcile)
            {
                await host.Services.GetRequiredService<SchedulerLoop>().Reconciled.WaitAsync(ReconcileWait, ct);
            }

            return new E2EFixture(root, processes, host, pipeName, callers);
        }
        catch
        {
            if (host is not null)
            {
                await ServiceHostFactory.StopAsync(host);
                host.Dispose();
            }

            GapActivatorDatabase.ReleasePooledConnections();
            await root.DisposeAsync();
            throw;
        }
    }

    /// <summary>Initializes the database in <paramref name="dataRoot"/> and writes the seed; returns the process ids by name.</summary>
    public static async Task<IReadOnlyDictionary<string, ProcessId>> SeedAsync(string dataRoot, CancellationToken ct)
    {
        await using E2ESeeding seeding = E2ESeeding.Open(dataRoot);
        await seeding.InitializeDatabaseAsync(ct);

        InstallationSettings defaults = InstallationSettings.Default();
        EmailAlertSettings email = EmailAlertSettings.Disabled;
        await seeding.Send<UpdateSettings, Unit>(
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
            ct);

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        ScheduleInput dueSoon = new(null, ScheduleInputKind.OneTime, now + TimeSpan.FromSeconds(3), null);
        ProcessDefinition[] definitions =
        [
            Definition(ScheduledName, ChildPaths.SampleChild, "1", null, null, [dueSoon]),
            Definition(LongName, ChildPaths.SampleChild, "60", null, null, []),
            Definition(StubbornName, ChildPaths.TestChild, "ignore-cancel", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), []),
        ];

        var processes = new Dictionary<string, ProcessId>(StringComparer.Ordinal);
        foreach (ProcessDefinition definition in definitions)
        {
            ProcessSaved saved = await seeding.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(definition), ct);
            processes.Add(definition.Name, saved.Id);
        }

        return processes;
    }

    /// <summary>Polls the runs of one process every 200 ms until one matches <paramref name="predicate"/>.</summary>
    public static async Task<RunRow> WaitForRunAsync(
        IQueryDispatcher queries,
        ProcessId processId,
        Func<RunRow, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(predicate);

        var filter = new RunFilter(processId, null, null, null, 1, 200);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            RunRowPage page = await queries.QueryAsync<ListRuns, RunRowPage>(new ListRuns(filter), Caller.System, ct);
            if (page.Items.FirstOrDefault(predicate) is { } match)
            {
                return match;
            }

            if (watch.Elapsed > timeout)
            {
                throw new TimeoutException(
                    $"No run matched within {timeout}. Runs: [{string.Join("; ", page.Items)}]");
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>Connects a client to the host's pipe; the fixture disposes it. The server is not verified against the SCM.</summary>
    public async Task<GapActivatorClient> ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        GapActivatorClient client = await GapActivatorClient.ConnectAsync(
            new GapActivatorClientOptions
            {
                PipeName = PipeName,
                ConnectTimeout = timeout,
                ServiceName = "GapActivator.Tests.NotInstalled",
            },
            ct);
        lock (_clients)
        {
            _clients.Add(client);
        }

        return client;
    }

    public ProcessId ProcessId(string name) => _processes[name];

    public Task<RunRow> WaitForRunAsync(string processName, Func<RunRow, bool> predicate, TimeSpan timeout, CancellationToken ct) =>
        WaitForRunAsync(_host.Services.GetRequiredService<IQueryDispatcher>(), ProcessId(processName), predicate, timeout, ct);

    public Task<TResult> Send<TCommand, TResult>(TCommand command, CancellationToken ct)
        where TCommand : ICommand<TResult> =>
        _host.Services.GetRequiredService<ICommandDispatcher>().SendAsync<TCommand, TResult>(command, Caller.System, ct);

    public Task<TResult> Query<TQuery, TResult>(TQuery query, CancellationToken ct)
        where TQuery : IQuery<TResult> =>
        _host.Services.GetRequiredService<IQueryDispatcher>().QueryAsync<TQuery, TResult>(query, Caller.System, ct);

    /// <summary>The PID of the active run of a process; fails when it has none or it is not launched yet.</summary>
    public async Task<int> ActivePidAsync(string processName, CancellationToken ct)
    {
        ProcessDetails details = await Query<GetProcess, ProcessDetails>(new GetProcess(ProcessId(processName)), ct);
        return details.Summary.ActiveRun?.Pid
            ?? throw new InvalidOperationException($"'{processName}' has no launched active run.");
    }

    /// <summary>Stops the host, bounded to 30 seconds. Safe to call more than once.</summary>
    public async Task StopHostAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await ServiceHostFactory.StopAsync(_host);
    }

    /// <summary>
    /// Stops and disposes the host and deletes the data root. A child the stop left running is killed, and then reported
    /// as a failure: the host must never leave one behind.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        GapActivatorClient[] clients;
        lock (_clients)
        {
            clients = [.. _clients];
            _clients.Clear();
        }

        foreach (GapActivatorClient client in clients)
        {
            await client.DisposeAsync();
        }

        List<DiagnosticsProcess> children = _stopped ? [] : await OpenActiveChildrenAsync();
        List<int> leftovers;
        try
        {
            await StopHostAsync();
        }
        finally
        {
            _host.Dispose();
            leftovers = KillLeftovers(children);
            GapActivatorDatabase.ReleasePooledConnections();
            await _root.DisposeAsync();
        }

        if (leftovers.Count > 0)
        {
            throw new InvalidOperationException(
                $"The host stopped but left children running (killed now): PIDs {string.Join(", ", leftovers)}.");
        }
    }

    private static ProcessDefinition Definition(
        string name,
        string executable,
        string arguments,
        TimeSpan? timeLimit,
        TimeSpan? gracePeriod,
        IReadOnlyList<ScheduleInput> schedules) =>
        new(name, "", ProcessKind.RunToCompletion, executable, arguments, null, [], timeLimit, gracePeriod, schedules);

    private static List<int> KillLeftovers(List<DiagnosticsProcess> children)
    {
        var leftovers = new List<int>();
        foreach (DiagnosticsProcess child in children)
        {
            using (child)
            {
                if (child.HasExited)
                {
                    continue;
                }

                leftovers.Add(child.Id);
                child.Kill(entireProcessTree: true);
                child.WaitForExit(ChildExitWait);
            }
        }

        return leftovers;
    }

    /// <summary>
    /// Handles to the children of the active runs, taken before the stop so a leftover can be found afterwards. Only a
    /// process with a child's name is taken, so a reused PID never points at an unrelated process.
    /// </summary>
    private async Task<List<DiagnosticsProcess>> OpenActiveChildrenAsync()
    {
        var children = new List<DiagnosticsProcess>();
        IReadOnlyList<ProcessSummary> summaries;
        try
        {
            summaries = await Query<ListProcesses, IReadOnlyList<ProcessSummary>>(new ListProcesses(), CancellationToken.None);
        }
        catch (Exception)
        {
            // The stop below still runs; the safety net is best-effort.
            return children;
        }

        foreach (int pid in summaries.Select(s => s.ActiveRun?.Pid).OfType<int>())
        {
            try
            {
                DiagnosticsProcess child = DiagnosticsProcess.GetProcessById(pid);
                if (ChildProcessNames.Contains(child.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    children.Add(child);
                }
                else
                {
                    child.Dispose();
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // Already gone.
            }
        }

        return children;
    }
}

/// <summary>
/// The application and infrastructure on a data root, outside any host: real time, a fake service control and no
/// logging output. It writes the seed before a host starts and reads the database while or after a host runs.
/// </summary>
internal sealed class E2ESeeding : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private E2ESeeding(ServiceProvider services) => _services = services;

    /// <summary>Opens the data root without touching it; call <see cref="InitializeDatabaseAsync"/> to create the database.</summary>
    public static E2ESeeding Open(string dataRoot)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new InfrastructureOptions(new GapActivatorPaths(dataRoot)));
        services.AddSingleton<IServiceControl, FakeServiceControl>();
        services.AddSingleton<IAppNotifier, RecordingAppNotifier>();
        return new E2ESeeding(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
    }

    public async Task InitializeDatabaseAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(ct);
    }

    public Task<TResult> Send<TCommand, TResult>(TCommand command, CancellationToken ct)
        where TCommand : ICommand<TResult> =>
        _services.GetRequiredService<ICommandDispatcher>().SendAsync<TCommand, TResult>(command, Caller.System, ct);

    public Task<TResult> Query<TQuery, TResult>(TQuery query, CancellationToken ct)
        where TQuery : IQuery<TResult> =>
        _services.GetRequiredService<IQueryDispatcher>().QueryAsync<TQuery, TResult>(query, Caller.System, ct);

    /// <summary>The stored (protected) e-mail password: <c>Email.Password?.Ciphertext</c>, or null when none is set.</summary>
    public async Task<string?> StoredEmailPasswordAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        InstallationSettings settings = await scope.ServiceProvider.GetRequiredService<ISettingsRepository>().GetAsync(ct);
        return settings.Email.Password?.Ciphertext;
    }

    public Task<RunRow> WaitForRunAsync(ProcessId processId, Func<RunRow, bool> predicate, TimeSpan timeout, CancellationToken ct) =>
        E2EFixture.WaitForRunAsync(_services.GetRequiredService<IQueryDispatcher>(), processId, predicate, timeout, ct);

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        GapActivatorDatabase.ReleasePooledConnections();
    }
}
