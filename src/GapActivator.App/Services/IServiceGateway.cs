using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

/// <summary>
/// The only way from the app to the service: it owns the connection, reconnects, and exposes the client typed calls. While
/// not connected, every call throws <c>GapActivatorRequestException</c> with the retryable code <c>disconnected</c>.
/// </summary>
public interface IServiceGateway : IAsyncDisposable
{
    GatewayStatus Status { get; }

    /// <summary>Raised through the UI dispatcher, only when <see cref="Status"/> changes.</summary>
    event EventHandler<GatewayStatus>? ConnectionChanged;

    /// <summary>Raised through the UI dispatcher.</summary>
    event EventHandler<NotificationDto>? NotificationReceived;

    /// <summary>Raised through the UI dispatcher.</summary>
    event EventHandler<StateChangedDto>? StateChanged;

    /// <summary>Starts the connect loop; a second call does nothing.</summary>
    void Start();

    Task<ProcessSavedDto> RegisterProcessAsync(ProcessDefinitionDto definition, CancellationToken ct = default);

    Task<ProcessSavedDto> UpdateProcessAsync(Guid id, ProcessDefinitionDto definition, CancellationToken ct = default);

    Task DeleteProcessAsync(Guid id, CancellationToken ct = default);

    Task EnableProcessAsync(Guid id, CancellationToken ct = default);

    Task DisableProcessAsync(Guid id, bool cancelActiveRun, CancellationToken ct = default);

    Task UnblockProcessAsync(Guid id, bool runNow, CancellationToken ct = default);

    Task<Guid> RunNowAsync(Guid processId, CancellationToken ct = default);

    Task CancelRunAsync(Guid runId, CancellationToken ct = default);

    Task RestartRunAsync(Guid processId, CancellationToken ct = default);

    Task PauseInstallationAsync(CancellationToken ct = default);

    Task ResumeInstallationAsync(CancellationToken ct = default);

    Task UpdateSettingsAsync(SettingsInputDto settings, CancellationToken ct = default);

    Task SendTestEmailAsync(EmailInputDto email, CancellationToken ct = default);

    Task RestartServiceAsync(CancellationToken ct = default);

    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ProcessSummaryDto>> ListProcessesAsync(CancellationToken ct = default);

    Task<ProcessDetailsDto> GetProcessAsync(Guid id, CancellationToken ct = default);

    Task<RunRowPageDto> ListRunsAsync(RunFilterDto filter, CancellationToken ct = default);

    Task<RunDetailsDto> GetRunAsync(Guid id, CancellationToken ct = default);

    Task<SettingsViewDto> GetSettingsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(string rule, DateTimeOffset anchor, int count, CancellationToken ct = default);
}
