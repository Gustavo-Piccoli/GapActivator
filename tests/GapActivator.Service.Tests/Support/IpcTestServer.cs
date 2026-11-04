using GapActivator.Ipc;
using GapActivator.Service.Ipc;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GapActivator.Service.Tests.Support;

/// <summary>
/// The Application test host plus a running <see cref="IpcServer"/> on a unique pipe, with a scripted caller resolver,
/// a recording lifetime and a recording exit-code sink.
/// </summary>
internal sealed class IpcTestServer : IAsyncDisposable
{
    private const string UninstalledServiceName = "GapActivator.Tests.NotInstalled";

    private readonly CancellationTokenSource _stopping = new();
    private readonly List<int> _exitCodes;

    private IpcTestServer(
        string pipeName,
        ApplicationTestHost host,
        List<int> exitCodes,
        Task ready)
    {
        PipeName = pipeName;
        Host = host;
        _exitCodes = exitCodes;
        Callers = (FakeCallerResolver)host.Services.GetRequiredService<ICallerResolver>();
        Registry = host.Services.GetRequiredService<IpcConnectionRegistry>();
        Lifetime = host.Services.GetRequiredService<RecordingLifetime>();

        IpcServer server = host.Services.GetRequiredService<IpcServer>();
        Running = Task.Run(() => server.RunAsync(ready, _stopping.Token));
    }

    public string PipeName { get; }

    public ApplicationTestHost Host { get; }

    public FakeCallerResolver Callers { get; }

    public IpcConnectionRegistry Registry { get; }

    public RecordingLifetime Lifetime { get; }

    public Task Running { get; }

    public IReadOnlyList<int> ExitCodes
    {
        get
        {
            lock (_exitCodes)
            {
                return [.. _exitCodes];
            }
        }
    }

    public static Task<IpcTestServer> StartAsync(
        Action<IServiceCollection>? configure = null,
        Func<IpcOptions, IpcOptions>? options = null,
        Task? ready = null)
    {
        string pipeName = $"GapActivator.Tests.{Guid.NewGuid():N}";
        var exitCodes = new List<int>();
        IpcOptions ipc = options?.Invoke(new IpcOptions { PipeName = pipeName }) ?? new IpcOptions { PipeName = pipeName };

        var host = new ApplicationTestHost(services =>
        {
            ScriptedCommandDispatcher.Install(services);
            services.AddSingleton<RecordingLifetime>();
            services.AddSingleton<IHostApplicationLifetime>(sp => sp.GetRequiredService<RecordingLifetime>());
            services.AddSingleton(new ExitCodeSink(code =>
            {
                lock (exitCodes)
                {
                    exitCodes.Add(code);
                }
            }));
            services.AddSingleton<HostRestart>();
            services.AddSingleton<FakeCallerResolver>();
            services.AddSingleton<ICallerResolver>(sp => sp.GetRequiredService<FakeCallerResolver>());
            services.AddGapActivatorIpc(ipc, new AuthorizationOptions(OperatorsGroupSetting.Auto));
            configure?.Invoke(services);
        });

        return Task.FromResult(new IpcTestServer(ipc.PipeName, host, exitCodes, ready ?? Task.CompletedTask));
    }

    public Task<GapActivatorClient> ConnectAsync(TimeSpan? timeout = null) =>
        GapActivatorClient.ConnectAsync(new GapActivatorClientOptions
        {
            PipeName = PipeName,
            ConnectTimeout = timeout ?? TimeSpan.FromSeconds(5),
            ServiceName = UninstalledServiceName,
        });

    public Task<RawPipeClient> ConnectRawAsync() => RawPipeClient.ConnectAsync(PipeName);

    /// <summary>Stops the server the way the host does and waits for <see cref="Running"/> to end.</summary>
    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        await Running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try
        {
            await Running.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception)
        {
            // A failed run is asserted by the test that caused it; disposal only has to clean up.
        }

        await Host.DisposeAsync();
        _stopping.Dispose();
    }
}
