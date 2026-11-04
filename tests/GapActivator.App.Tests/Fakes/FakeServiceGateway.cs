using GapActivator.App.Services;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Fakes;

/// <summary>An in-memory gateway for view-model tests: records every call, returns settable results and scripts failures and holds.</summary>
internal sealed class FakeServiceGateway : IServiceGateway
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Exception> _failures = [];
    private readonly Dictionary<string, TaskCompletionSource> _holds = [];
    private readonly List<(string Method, object?[] Args)> _calls = [];

    public FakeServiceGateway() => Status = Operator();

    public event EventHandler<GatewayStatus>? ConnectionChanged;

    public event EventHandler<NotificationDto>? NotificationReceived;

    public event EventHandler<StateChangedDto>? StateChanged;

    public GatewayStatus Status { get; private set; }

    public List<(string Method, object?[] Args)> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public DashboardDto Dashboard { get; set; } = Dtos.Dashboard();

    public List<ProcessSummaryDto> Processes { get; set; } = [];

    public Dictionary<Guid, ProcessDetailsDto> Details { get; set; } = [];

    public Func<RunFilterDto, RunRowPageDto> RunPages { get; set; } = _ => new RunRowPageDto([], 0);

    public Dictionary<Guid, RunDetailsDto> Runs { get; set; } = [];

    public SettingsViewDto Settings { get; set; } = Dtos.SettingsView();

    public Func<string, DateTimeOffset, int, IReadOnlyList<DateTimeOffset>> Preview { get; set; } = (_, _, _) => [];

    public Guid NextRunId { get; set; } = Guid.NewGuid();

    /// <summary>The result of register and update; when null, the new id with no shared executables.</summary>
    public ProcessSavedDto? Saved { get; set; }

    public static GatewayStatus Operator(OperatorsGroupMode mode = OperatorsGroupMode.Enabled) =>
        Connected(new CallerDto("EXAMPLE\\operator", true), mode, verified: true);

    public static GatewayStatus Viewer(OperatorsGroupMode mode = OperatorsGroupMode.Enabled) =>
        Connected(new CallerDto("EXAMPLE\\viewer", false), mode, verified: true);

    public static GatewayStatus Stopped { get; } = GatewayStatus.Initial with { State = ConnectionState.ServiceStopped };

    public static GatewayStatus NotInstalled { get; } = GatewayStatus.Initial with { State = ConnectionState.ServiceNotInstalled };

    public static GatewayStatus DevOperator { get; } =
        Connected(new CallerDto("EXAMPLE\\operator", true), OperatorsGroupMode.Enabled, verified: false);

    public void FailNext(string method, Exception error)
    {
        lock (_gate)
        {
            _failures[method] = error;
        }
    }

    /// <summary>Makes the next call of <paramref name="method"/> wait until the returned source is completed.</summary>
    public TaskCompletionSource Hold(string method)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _holds[method] = hold;
        }

        return hold;
    }

    public void SetStatus(GatewayStatus status)
    {
        Status = status;
        ConnectionChanged?.Invoke(this, status);
    }

    public void RaiseStateChanged(StateChangedDto state) => StateChanged?.Invoke(this, state);

    public void RaiseNotification(NotificationDto notification) => NotificationReceived?.Invoke(this, notification);

    public void Start() => Record(nameof(Start)).GetAwaiter().GetResult();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<ProcessSavedDto> RegisterProcessAsync(ProcessDefinitionDto definition, CancellationToken ct = default)
    {
        await Record(nameof(RegisterProcessAsync), definition).ConfigureAwait(false);
        return Saved ?? new ProcessSavedDto(Guid.NewGuid(), []);
    }

    public async Task<ProcessSavedDto> UpdateProcessAsync(Guid id, ProcessDefinitionDto definition, CancellationToken ct = default)
    {
        await Record(nameof(UpdateProcessAsync), id, definition).ConfigureAwait(false);
        return Saved ?? new ProcessSavedDto(id, []);
    }

    public Task DeleteProcessAsync(Guid id, CancellationToken ct = default) => Record(nameof(DeleteProcessAsync), id);

    public Task EnableProcessAsync(Guid id, CancellationToken ct = default) => Record(nameof(EnableProcessAsync), id);

    public Task DisableProcessAsync(Guid id, bool cancelActiveRun, CancellationToken ct = default) =>
        Record(nameof(DisableProcessAsync), id, cancelActiveRun);

    public Task UnblockProcessAsync(Guid id, bool runNow, CancellationToken ct = default) =>
        Record(nameof(UnblockProcessAsync), id, runNow);

    public async Task<Guid> RunNowAsync(Guid processId, CancellationToken ct = default)
    {
        await Record(nameof(RunNowAsync), processId).ConfigureAwait(false);
        return NextRunId;
    }

    public Task CancelRunAsync(Guid runId, CancellationToken ct = default) => Record(nameof(CancelRunAsync), runId);

    public Task RestartRunAsync(Guid processId, CancellationToken ct = default) => Record(nameof(RestartRunAsync), processId);

    public Task PauseInstallationAsync(CancellationToken ct = default) => Record(nameof(PauseInstallationAsync));

    public Task ResumeInstallationAsync(CancellationToken ct = default) => Record(nameof(ResumeInstallationAsync));

    public Task UpdateSettingsAsync(SettingsInputDto settings, CancellationToken ct = default) =>
        Record(nameof(UpdateSettingsAsync), settings);

    public Task SendTestEmailAsync(EmailInputDto email, CancellationToken ct = default) => Record(nameof(SendTestEmailAsync), email);

    public Task RestartServiceAsync(CancellationToken ct = default) => Record(nameof(RestartServiceAsync));

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        await Record(nameof(GetDashboardAsync)).ConfigureAwait(false);
        return Dashboard;
    }

    public async Task<IReadOnlyList<ProcessSummaryDto>> ListProcessesAsync(CancellationToken ct = default)
    {
        await Record(nameof(ListProcessesAsync)).ConfigureAwait(false);
        return [.. Processes];
    }

    public async Task<ProcessDetailsDto> GetProcessAsync(Guid id, CancellationToken ct = default)
    {
        await Record(nameof(GetProcessAsync), id).ConfigureAwait(false);
        return Details.TryGetValue(id, out ProcessDetailsDto? details)
            ? details
            : throw new GapActivatorRequestException(IpcErrorCodes.NotFound, "The process does not exist.", retryable: false);
    }

    public async Task<RunRowPageDto> ListRunsAsync(RunFilterDto filter, CancellationToken ct = default)
    {
        await Record(nameof(ListRunsAsync), filter).ConfigureAwait(false);
        return RunPages(filter);
    }

    public async Task<RunDetailsDto> GetRunAsync(Guid id, CancellationToken ct = default)
    {
        await Record(nameof(GetRunAsync), id).ConfigureAwait(false);
        return Runs.TryGetValue(id, out RunDetailsDto? run)
            ? run
            : throw new GapActivatorRequestException(IpcErrorCodes.NotFound, "The run does not exist.", retryable: false);
    }

    public async Task<SettingsViewDto> GetSettingsAsync(CancellationToken ct = default)
    {
        await Record(nameof(GetSettingsAsync)).ConfigureAwait(false);
        return Settings;
    }

    public async Task<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(string rule, DateTimeOffset anchor, int count, CancellationToken ct = default)
    {
        await Record(nameof(PreviewScheduleAsync), rule, anchor, count).ConfigureAwait(false);
        return Preview(rule, anchor, count);
    }

    private static GatewayStatus Connected(CallerDto caller, OperatorsGroupMode mode, bool verified) =>
        new(ConnectionState.Connected, verified, caller, mode, "0.0.0", null);

    private async Task Record(string method, params object?[] args)
    {
        Exception? failure;
        TaskCompletionSource? hold;
        lock (_gate)
        {
            _calls.Add((method, args));
            _failures.Remove(method, out failure);
            _holds.Remove(method, out hold);
        }

        if (hold is not null)
        {
            await hold.Task.ConfigureAwait(false);
        }

        if (failure is not null)
        {
            throw failure;
        }
    }
}
