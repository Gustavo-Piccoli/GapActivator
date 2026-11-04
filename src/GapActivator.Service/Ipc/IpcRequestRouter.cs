using System.Text.Json;
using GapActivator.Application.Messaging;
using GapActivator.Application.Processes;
using GapActivator.Application.Queries;
using GapActivator.Application.Runs;
using GapActivator.Application.Settings;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Ipc;

/// <summary>One message the router serves; <paramref name="DefersRestart"/> marks the one whose response is written before the service restarts.</summary>
internal sealed record IpcRoute(string Name, Type ApplicationType, bool IsCommand, bool DefersRestart);

/// <summary>
/// Maps each request DTO to its public Application command or query, sends it as the resolved caller and maps the
/// result back. Only the routes registered here are reachable, so an internal command never is.
/// </summary>
internal sealed class IpcRequestRouter
{
    private readonly ICommandDispatcher _commands;
    private readonly IQueryDispatcher _queries;
    private readonly Dictionary<string, Registered> _routes = new(StringComparer.Ordinal);

    public IpcRequestRouter(ICommandDispatcher commands, IQueryDispatcher queries)
    {
        _commands = commands;
        _queries = queries;

        Command<RegisterProcessRequest, RegisterProcess, ProcessSaved, ProcessSavedDto>(
            IpcMessages.RegisterProcess, r => new RegisterProcess(r.Definition.ToApplication()), s => s.ToWire());
        Command<UpdateProcessRequest, UpdateProcess, ProcessSaved, ProcessSavedDto>(
            IpcMessages.UpdateProcess, r => new UpdateProcess(new ProcessId(r.Id), r.Definition.ToApplication()), s => s.ToWire());
        Command<DeleteProcessRequest, DeleteProcess, Unit, EmptyResponse>(
            IpcMessages.DeleteProcess, r => new DeleteProcess(new ProcessId(r.Id)), u => u.ToWire());
        Command<EnableProcessRequest, EnableProcess, Unit, EmptyResponse>(
            IpcMessages.EnableProcess, r => new EnableProcess(new ProcessId(r.Id)), u => u.ToWire());
        Command<DisableProcessRequest, DisableProcess, Unit, EmptyResponse>(
            IpcMessages.DisableProcess, r => new DisableProcess(new ProcessId(r.Id), r.CancelActiveRun), u => u.ToWire());
        Command<UnblockProcessRequest, UnblockProcess, Unit, EmptyResponse>(
            IpcMessages.UnblockProcess, r => new UnblockProcess(new ProcessId(r.Id), r.RunNow), u => u.ToWire());
        Command<RunNowRequest, RunNow, RunId, RunNowResponse>(
            IpcMessages.RunNow, r => new RunNow(new ProcessId(r.Id)), id => id.ToWire());
        Command<CancelRunRequest, CancelRun, Unit, EmptyResponse>(
            IpcMessages.CancelRun, r => new CancelRun(new RunId(r.Id)), u => u.ToWire());
        Command<RestartRunRequest, RestartRun, Unit, EmptyResponse>(
            IpcMessages.RestartRun, r => new RestartRun(new ProcessId(r.Id)), u => u.ToWire());
        Command<PauseInstallationRequest, PauseInstallation, Unit, EmptyResponse>(
            IpcMessages.PauseInstallation, _ => new PauseInstallation(), u => u.ToWire());
        Command<ResumeInstallationRequest, ResumeInstallation, Unit, EmptyResponse>(
            IpcMessages.ResumeInstallation, _ => new ResumeInstallation(), u => u.ToWire());
        Command<UpdateSettingsRequest, UpdateSettings, Unit, EmptyResponse>(
            IpcMessages.UpdateSettings, r => new UpdateSettings(r.Settings.ToApplication()), u => u.ToWire());
        Command<SendTestEmailRequest, SendTestEmail, Unit, EmptyResponse>(
            IpcMessages.SendTestEmail, r => new SendTestEmail(r.Email.ToApplication()), u => u.ToWire());
        Command<RestartServiceRequest, RestartGapActivator, Unit, EmptyResponse>(
            IpcMessages.RestartService, _ => new RestartGapActivator(), u => u.ToWire(), defersRestart: true);

        Query<GetDashboardRequest, GetDashboard, Dashboard, DashboardDto>(
            IpcMessages.GetDashboard, _ => new GetDashboard(), d => d.ToWire());
        Query<ListProcessesRequest, ListProcesses, IReadOnlyList<ProcessSummary>, ListProcessesResponse>(
            IpcMessages.ListProcesses, _ => new ListProcesses(), l => l.ToListResponse());
        Query<GetProcessRequest, GetProcess, ProcessDetails, ProcessDetailsDto>(
            IpcMessages.GetProcess, r => new GetProcess(new ProcessId(r.Id)), d => d.ToWire());
        Query<ListRunsRequest, ListRuns, RunRowPage, RunRowPageDto>(
            IpcMessages.ListRuns, r => new ListRuns(r.Filter.ToApplication()), p => p.ToWire());
        Query<GetRunRequest, GetRun, RunDetails, RunDetailsDto>(
            IpcMessages.GetRun, r => new GetRun(new RunId(r.Id)), d => d.ToWire());
        Query<GetSettingsRequest, GetSettings, SettingsView, SettingsViewDto>(
            IpcMessages.GetSettings, _ => new GetSettings(), v => v.ToWire());
        Query<PreviewScheduleRequest, PreviewSchedule, IReadOnlyList<DateTimeOffset>, PreviewScheduleResponse>(
            IpcMessages.PreviewSchedule, r => new PreviewSchedule(r.Rule, r.Anchor, r.Count), o => o.ToPreviewResponse());
    }

    private delegate Task<JsonElement> Handler(JsonElement? payload, Caller caller, CancellationToken ct);

    private sealed record Registered(IpcRoute Route, Handler Handler);

    public IReadOnlyCollection<IpcRoute> Routes => [.. _routes.Values.Select(r => r.Route)];

    /// <exception cref="IpcUnknownMessageException">No route has this name; <c>Hello</c> belongs to the connection itself.</exception>
    public IpcRoute Find(string type) =>
        type is not null && _routes.TryGetValue(type, out Registered? registered)
            ? registered.Route
            : throw new IpcUnknownMessageException(type ?? string.Empty);

    /// <summary>Binds the payload, runs the command or query as <paramref name="caller"/> and returns the response payload.</summary>
    public Task<JsonElement> DispatchAsync(IpcRoute route, JsonElement? payload, Caller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(caller);
        return _routes[route.Name].Handler(payload, caller, ct);
    }

    private void Command<TRequest, TCommand, TResult, TResponse>(
        IpcMessage<TRequest, TResponse> message,
        Func<TRequest, TCommand> toCommand,
        Func<TResult, TResponse> toResponse,
        bool defersRestart = false)
        where TCommand : ICommand<TResult> =>
        Add(new IpcRoute(message.Name, typeof(TCommand), true, defersRestart), async (payload, caller, ct) =>
        {
            TRequest request = IpcCodec.ReadPayload(payload, message.RequestType);
            TResult result = await _commands.SendAsync<TCommand, TResult>(toCommand(request), caller, ct);
            return IpcCodec.ToElement(toResponse(result), message.ResponseType);
        });

    private void Query<TRequest, TQuery, TResult, TResponse>(
        IpcMessage<TRequest, TResponse> message,
        Func<TRequest, TQuery> toQuery,
        Func<TResult, TResponse> toResponse)
        where TQuery : IQuery<TResult> =>
        Add(new IpcRoute(message.Name, typeof(TQuery), false, false), async (payload, caller, ct) =>
        {
            TRequest request = IpcCodec.ReadPayload(payload, message.RequestType);
            TResult result = await _queries.QueryAsync<TQuery, TResult>(toQuery(request), caller, ct);
            return IpcCodec.ToElement(toResponse(result), message.ResponseType);
        });

    private void Add(IpcRoute route, Handler handler) => _routes.Add(route.Name, new Registered(route, handler));
}
