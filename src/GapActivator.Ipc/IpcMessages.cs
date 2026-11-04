using GapActivator.Ipc.Contracts;

namespace GapActivator.Ipc;

/// <summary>Every message on the wire; each property is named like the <c>type</c> it carries.</summary>
public static class IpcMessages
{
    public static IpcMessage<HelloRequest, HelloResponse> Hello { get; } =
        new("Hello", IpcJsonContext.Default.HelloRequest, IpcJsonContext.Default.HelloResponse);

    public static IpcMessage<RegisterProcessRequest, ProcessSavedDto> RegisterProcess { get; } =
        new("RegisterProcess", IpcJsonContext.Default.RegisterProcessRequest, IpcJsonContext.Default.ProcessSavedDto);

    public static IpcMessage<UpdateProcessRequest, ProcessSavedDto> UpdateProcess { get; } =
        new("UpdateProcess", IpcJsonContext.Default.UpdateProcessRequest, IpcJsonContext.Default.ProcessSavedDto);

    public static IpcMessage<DeleteProcessRequest, EmptyResponse> DeleteProcess { get; } =
        new("DeleteProcess", IpcJsonContext.Default.DeleteProcessRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<EnableProcessRequest, EmptyResponse> EnableProcess { get; } =
        new("EnableProcess", IpcJsonContext.Default.EnableProcessRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<DisableProcessRequest, EmptyResponse> DisableProcess { get; } =
        new("DisableProcess", IpcJsonContext.Default.DisableProcessRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<UnblockProcessRequest, EmptyResponse> UnblockProcess { get; } =
        new("UnblockProcess", IpcJsonContext.Default.UnblockProcessRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<RunNowRequest, RunNowResponse> RunNow { get; } =
        new("RunNow", IpcJsonContext.Default.RunNowRequest, IpcJsonContext.Default.RunNowResponse);

    public static IpcMessage<CancelRunRequest, EmptyResponse> CancelRun { get; } =
        new("CancelRun", IpcJsonContext.Default.CancelRunRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<RestartRunRequest, EmptyResponse> RestartRun { get; } =
        new("RestartRun", IpcJsonContext.Default.RestartRunRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<PauseInstallationRequest, EmptyResponse> PauseInstallation { get; } =
        new("PauseInstallation", IpcJsonContext.Default.PauseInstallationRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<ResumeInstallationRequest, EmptyResponse> ResumeInstallation { get; } =
        new("ResumeInstallation", IpcJsonContext.Default.ResumeInstallationRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<UpdateSettingsRequest, EmptyResponse> UpdateSettings { get; } =
        new("UpdateSettings", IpcJsonContext.Default.UpdateSettingsRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<SendTestEmailRequest, EmptyResponse> SendTestEmail { get; } =
        new("SendTestEmail", IpcJsonContext.Default.SendTestEmailRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<RestartServiceRequest, EmptyResponse> RestartService { get; } =
        new("RestartService", IpcJsonContext.Default.RestartServiceRequest, IpcJsonContext.Default.EmptyResponse);

    public static IpcMessage<GetDashboardRequest, DashboardDto> GetDashboard { get; } =
        new("GetDashboard", IpcJsonContext.Default.GetDashboardRequest, IpcJsonContext.Default.DashboardDto);

    public static IpcMessage<ListProcessesRequest, ListProcessesResponse> ListProcesses { get; } =
        new("ListProcesses", IpcJsonContext.Default.ListProcessesRequest, IpcJsonContext.Default.ListProcessesResponse);

    public static IpcMessage<GetProcessRequest, ProcessDetailsDto> GetProcess { get; } =
        new("GetProcess", IpcJsonContext.Default.GetProcessRequest, IpcJsonContext.Default.ProcessDetailsDto);

    public static IpcMessage<ListRunsRequest, RunRowPageDto> ListRuns { get; } =
        new("ListRuns", IpcJsonContext.Default.ListRunsRequest, IpcJsonContext.Default.RunRowPageDto);

    public static IpcMessage<GetRunRequest, RunDetailsDto> GetRun { get; } =
        new("GetRun", IpcJsonContext.Default.GetRunRequest, IpcJsonContext.Default.RunDetailsDto);

    public static IpcMessage<GetSettingsRequest, SettingsViewDto> GetSettings { get; } =
        new("GetSettings", IpcJsonContext.Default.GetSettingsRequest, IpcJsonContext.Default.SettingsViewDto);

    public static IpcMessage<PreviewScheduleRequest, PreviewScheduleResponse> PreviewSchedule { get; } =
        new("PreviewSchedule", IpcJsonContext.Default.PreviewScheduleRequest, IpcJsonContext.Default.PreviewScheduleResponse);

    /// <summary>The <c>type</c> values, in protocol-table order.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        Hello.Name,
        RegisterProcess.Name,
        UpdateProcess.Name,
        DeleteProcess.Name,
        EnableProcess.Name,
        DisableProcess.Name,
        UnblockProcess.Name,
        RunNow.Name,
        CancelRun.Name,
        RestartRun.Name,
        PauseInstallation.Name,
        ResumeInstallation.Name,
        UpdateSettings.Name,
        SendTestEmail.Name,
        RestartService.Name,
        GetDashboard.Name,
        ListProcesses.Name,
        GetProcess.Name,
        ListRuns.Name,
        GetRun.Name,
        GetSettings.Name,
        PreviewSchedule.Name,
    ];
}
