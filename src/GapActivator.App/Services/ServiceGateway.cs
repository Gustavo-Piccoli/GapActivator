using GapActivator.App.Shell;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Services;

/// <summary>
/// Keeps one connection to the service alive: connects, classifies why it cannot, retries with growing delays and forwards
/// the pushes. The loop runs off the UI context (<c>Task.Run</c>, <c>ConfigureAwait(false)</c>); events reach the UI only
/// through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class ServiceGateway : IServiceGateway
{
    private readonly GapActivatorClientOptions _options;
    private readonly IServiceStatusProbe _probe;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly ILogger<ServiceGateway> _logger;
    private readonly Func<GapActivatorClientOptions, CancellationToken, Task<GapActivatorClient>> _connect;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();

    private GatewayStatus _status = GatewayStatus.Initial;
    private GapActivatorClient? _client;
    private Task? _loop;
    private bool _disposed;

    public ServiceGateway(
        GapActivatorClientOptions options,
        IServiceStatusProbe probe,
        IUiDispatcher ui,
        TimeProvider time,
        ILogger<ServiceGateway> logger)
        : this(options, probe, ui, time, logger, (o, ct) => GapActivatorClient.ConnectAsync(o, ct))
    {
    }

    internal ServiceGateway(
        GapActivatorClientOptions options,
        IServiceStatusProbe probe,
        IUiDispatcher ui,
        TimeProvider time,
        ILogger<ServiceGateway> logger,
        Func<GapActivatorClientOptions, CancellationToken, Task<GapActivatorClient>> connect)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connect);
        _options = options;
        _probe = probe;
        _ui = ui;
        _time = time;
        _logger = logger;
        _connect = connect;
    }

    /// <summary>The wait after each failed attempt; the last delay repeats.</summary>
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)];

    public event EventHandler<GatewayStatus>? ConnectionChanged;

    public event EventHandler<NotificationDto>? NotificationReceived;

    public event EventHandler<StateChangedDto>? StateChanged;

    public GatewayStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null || _disposed)
            {
                return;
            }

            CancellationToken ct = _stop.Token;
            _loop = Task.Run(() => RunAsync(ct), CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loop = _loop;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (loop is not null)
        {
            await loop.ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    public Task<ProcessSavedDto> RegisterProcessAsync(ProcessDefinitionDto definition, CancellationToken ct = default) =>
        Connected().RegisterProcessAsync(definition, ct);

    public Task<ProcessSavedDto> UpdateProcessAsync(Guid id, ProcessDefinitionDto definition, CancellationToken ct = default) =>
        Connected().UpdateProcessAsync(id, definition, ct);

    public Task DeleteProcessAsync(Guid id, CancellationToken ct = default) => Connected().DeleteProcessAsync(id, ct);

    public Task EnableProcessAsync(Guid id, CancellationToken ct = default) => Connected().EnableProcessAsync(id, ct);

    public Task DisableProcessAsync(Guid id, bool cancelActiveRun, CancellationToken ct = default) =>
        Connected().DisableProcessAsync(id, cancelActiveRun, ct);

    public Task UnblockProcessAsync(Guid id, bool runNow, CancellationToken ct = default) =>
        Connected().UnblockProcessAsync(id, runNow, ct);

    public Task<Guid> RunNowAsync(Guid processId, CancellationToken ct = default) => Connected().RunNowAsync(processId, ct);

    public Task CancelRunAsync(Guid runId, CancellationToken ct = default) => Connected().CancelRunAsync(runId, ct);

    public Task RestartRunAsync(Guid processId, CancellationToken ct = default) => Connected().RestartRunAsync(processId, ct);

    public Task PauseInstallationAsync(CancellationToken ct = default) => Connected().PauseInstallationAsync(ct);

    public Task ResumeInstallationAsync(CancellationToken ct = default) => Connected().ResumeInstallationAsync(ct);

    public Task UpdateSettingsAsync(SettingsInputDto settings, CancellationToken ct = default) =>
        Connected().UpdateSettingsAsync(settings, ct);

    public Task SendTestEmailAsync(EmailInputDto email, CancellationToken ct = default) => Connected().SendTestEmailAsync(email, ct);

    public Task RestartServiceAsync(CancellationToken ct = default) => Connected().RestartServiceAsync(ct);

    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default) => Connected().GetDashboardAsync(ct);

    public Task<IReadOnlyList<ProcessSummaryDto>> ListProcessesAsync(CancellationToken ct = default) =>
        Connected().ListProcessesAsync(ct);

    public Task<ProcessDetailsDto> GetProcessAsync(Guid id, CancellationToken ct = default) => Connected().GetProcessAsync(id, ct);

    public Task<RunRowPageDto> ListRunsAsync(RunFilterDto filter, CancellationToken ct = default) => Connected().ListRunsAsync(filter, ct);

    public Task<RunDetailsDto> GetRunAsync(Guid id, CancellationToken ct = default) => Connected().GetRunAsync(id, ct);

    public Task<SettingsViewDto> GetSettingsAsync(CancellationToken ct = default) => Connected().GetSettingsAsync(ct);

    public Task<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(string rule, DateTimeOffset anchor, int count, CancellationToken ct = default) =>
        Connected().PreviewScheduleAsync(rule, anchor, count, ct);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Connecting to the service failed.")]
    private partial void LogConnectFailed(Exception exception);

    private GapActivatorClient Connected()
    {
        GapActivatorClient? client;
        lock (_gate)
        {
            client = _client;
        }

        return client ?? throw new GapActivatorRequestException(IpcErrorCodes.Disconnected, "Not connected to the service.", retryable: true);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                GapActivatorClient client = await _connect(_options, ct).ConfigureAwait(false);
                failures = 0;
                await HoldAsync(client, ct).ConfigureAwait(false);
                SetStatus(GatewayStatus.Initial);

                // The wait after a lost connection is the first delay of a new sequence.
                failures = 1;
                await Task.Delay(RetryDelays[0], _time, ct).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
                SetStatus(Classify(_probe));
            }
            catch (GapActivatorRequestException ex) when (IsRefusal(ex.Code))
            {
                SetStatus(GatewayStatus.Initial with { State = ConnectionState.Refused, ErrorCode = ex.Code });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogConnectFailed(ex);
                SetStatus(GatewayStatus.Initial);
            }

            try
            {
                await Task.Delay(RetryDelays[Math.Min(failures, RetryDelays.Count - 1)], _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            failures++;
        }
    }

    /// <summary>Publishes the connected state and holds the client until it disconnects or the gateway stops.</summary>
    private async Task HoldAsync(GapActivatorClient client, CancellationToken ct)
    {
        var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NotificationDto> onNotification = (_, n) => _ui.Post(() => NotificationReceived?.Invoke(this, n));
        EventHandler<StateChangedDto> onState = (_, s) => _ui.Post(() => StateChanged?.Invoke(this, s));
        EventHandler<DisconnectedEventArgs> onDisconnected = (_, _) => gone.TrySetResult();

        client.NotificationReceived += onNotification;
        client.StateChanged += onState;
        client.Disconnected += onDisconnected;
        try
        {
            lock (_gate)
            {
                _client = client;
            }

            SetStatus(new GatewayStatus(
                ConnectionState.Connected,
                client.ServerVerified,
                client.Server.Caller,
                client.Server.OperatorsGroupMode,
                client.Server.ServiceVersion,
                ErrorCode: null));

            await gone.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _client = null;
            }

            client.NotificationReceived -= onNotification;
            client.StateChanged -= onState;
            client.Disconnected -= onDisconnected;
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static GatewayStatus Classify(IServiceStatusProbe probe)
    {
        ServiceInstallState state;
        try
        {
            state = probe.Query();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            state = ServiceInstallState.Unknown;
        }

        return state switch
        {
            ServiceInstallState.NotInstalled => GatewayStatus.Initial with { State = ConnectionState.ServiceNotInstalled },
            ServiceInstallState.Stopped => GatewayStatus.Initial with { State = ConnectionState.ServiceStopped },
            _ => GatewayStatus.Initial,
        };
    }

    private static bool IsRefusal(string code) =>
        code is IpcErrorCodes.ServerNotVerified or IpcErrorCodes.ProtocolMismatch or IpcErrorCodes.Unauthorized or IpcErrorCodes.Busy;

    private void SetStatus(GatewayStatus status)
    {
        lock (_gate)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        _ui.Post(() => ConnectionChanged?.Invoke(this, status));
    }
}
