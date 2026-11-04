namespace GapActivator.Ipc.Contracts;

public sealed record HelloRequest(int ProtocolVersion, string ClientVersion);

public sealed record CallerDto(string Name, bool CanChange);

public sealed record HelloResponse(
    int ProtocolVersion,
    string ServiceVersion,
    CallerDto Caller,
    OperatorsGroupMode OperatorsGroupMode);

/// <summary>The response of every message whose Application result is Unit.</summary>
public sealed record EmptyResponse;

public sealed record RegisterProcessRequest(ProcessDefinitionDto Definition);

public sealed record UpdateProcessRequest(Guid Id, ProcessDefinitionDto Definition);

public sealed record DeleteProcessRequest(Guid Id);

public sealed record EnableProcessRequest(Guid Id);

public sealed record DisableProcessRequest(Guid Id, bool CancelActiveRun);

public sealed record UnblockProcessRequest(Guid Id, bool RunNow);

public sealed record RunNowRequest(Guid Id);

public sealed record RunNowResponse(Guid RunId);

public sealed record CancelRunRequest(Guid Id);

public sealed record RestartRunRequest(Guid Id);

public sealed record PauseInstallationRequest;

public sealed record ResumeInstallationRequest;

public sealed record UpdateSettingsRequest(SettingsInputDto Settings);

public sealed record SendTestEmailRequest(EmailInputDto Email);

public sealed record RestartServiceRequest;

public sealed record GetDashboardRequest;

public sealed record ListProcessesRequest;

public sealed record ListProcessesResponse(IReadOnlyList<ProcessSummaryDto> Items);

public sealed record GetProcessRequest(Guid Id);

public sealed record ListRunsRequest(RunFilterDto Filter);

public sealed record GetRunRequest(Guid Id);

public sealed record GetSettingsRequest;

public sealed record PreviewScheduleRequest(string Rule, DateTimeOffset Anchor, int Count);

public sealed record PreviewScheduleResponse(IReadOnlyList<DateTimeOffset> Occurrences);

public sealed record StateChangedDto(Guid? ProcessId, Guid? RunId);
