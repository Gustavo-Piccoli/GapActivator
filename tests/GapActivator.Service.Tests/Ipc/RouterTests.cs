using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GapActivator.Application;
using GapActivator.Application.Messaging;
using GapActivator.Application.Queries;
using GapActivator.Domain;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using GapActivator.Service.Ipc;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wire = GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Tests.Ipc;

public sealed class RouterTests
{
    private static readonly Caller Operator = Caller.Operator("operator");

    private static IpcRequestRouter RouterFor(ApplicationTestHost host) =>
        new(host.Services.GetRequiredService<ICommandDispatcher>(), host.Services.GetRequiredService<IQueryDispatcher>());

    private static IpcRequestRouter InertRouter(IQueryDispatcher? queries = null) =>
        new(new NullCommandDispatcher(), queries ?? new CapturingQueryDispatcher());

    private static JsonElement Element<T>(T value, JsonTypeInfo<T> type) => IpcCodec.ToElement(value, type);

    private static async Task<TResponse> CallAsync<TRequest, TResponse>(
        IpcRequestRouter router,
        IpcMessage<TRequest, TResponse> message,
        TRequest request,
        Caller caller)
    {
        JsonElement payload = await router.DispatchAsync(
            router.Find(message.Name), Element(request, message.RequestType), caller, CancellationToken.None);
        return IpcCodec.ReadPayload(payload, message.ResponseType);
    }

    private static ProcessDefinitionDto Definition(string name = "Nightly export") => new(
        name,
        "Exports data",
        Wire.ProcessKind.RunToCompletion,
        @"C:\Tools\sample.exe",
        "\"a b\" c",
        @"C:\Tools",
        [
            new EnvironmentVariableInputDto("PLAIN", "visible", false),
            new EnvironmentVariableInputDto("TOKEN", "hidden-value", true),
        ],
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(45),
        [
            new ScheduleInputDto(null, Wire.ScheduleInputKind.OneTime, ApplicationTestHost.StartTime.AddDays(1), null),
            new ScheduleInputDto(null, Wire.ScheduleInputKind.Recurring, ApplicationTestHost.StartTime, "FREQ=HOURLY"),
        ]);

    [Fact]
    public void Router_RouteNames_AreTheProtocolMessagesExceptHello()
    {
        IpcRequestRouter router = InertRouter();

        router.Routes.Select(r => r.Name).ShouldBe(IpcMessages.Names.Skip(1));
        router.Routes.Where(r => r.DefersRestart).Select(r => r.Name).ShouldBe([IpcMessages.RestartService.Name]);
    }

    [Fact]
    public void Router_MapsEveryPublicCommandAndQuery_ExceptTheCycleQueries()
    {
        IpcRequestRouter router = InertRouter();

        Type[] expected = [.. ApplicationAssembly.Reference.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && ImplementsMessage(t))
            .Where(t => !ImplementsOpen(t, typeof(IInternalCommand<>)))
            .Where(t => t != typeof(ListProcessIds) && t != typeof(GetCycleSettings))];

        expected.Length.ShouldBe(21);
        router.Routes.Select(r => r.ApplicationType).ShouldBe(expected, ignoreOrder: true);
        router.Routes.Count(r => r.IsCommand).ShouldBe(14);
    }

    [Fact]
    public void Router_MapsNoInternalCommand()
    {
        IpcRequestRouter router = InertRouter();

        router.Routes.Where(r => ImplementsOpen(r.ApplicationType, typeof(IInternalCommand<>))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("StopRun")]
    [InlineData("ReconcileOnStartup")]
    [InlineData("EvaluateProcess")]
    [InlineData("RecordRunExited")]
    [InlineData("PurgeHistory")]
    [InlineData("ListProcessIds")]
    [InlineData("GetCycleSettings")]
    [InlineData("Hello")]
    [InlineData("runnow")]
    public void Find_InternalOrUnknownName_ThrowsUnknownMessage(string name)
    {
        IpcRequestRouter router = InertRouter();

        var error = Should.Throw<IpcUnknownMessageException>(() => router.Find(name));

        error.Message.ShouldBe($"Unknown message type '{name}'.");
    }

    [Fact]
    public void WireShapes_MirrorTheApplicationShapes()
    {
        Type[] dtos = [.. IpcAssembly.Reference.GetTypes()
            .Where(t => t.Namespace == "GapActivator.Ipc.Contracts" && t.IsPublic
                && t.Name.EndsWith("Dto", StringComparison.Ordinal) && t.Name != "CallerDto" && t.Name != "StateChangedDto")];
        dtos.Length.ShouldBe(23);

        var problems = new List<string>();
        foreach (Type dto in dtos)
        {
            string stem = dto.Name == "NotificationDto" ? "AppNotification" : dto.Name[..^3];
            Type[] sources = [.. ApplicationAssembly.Reference.GetTypes().Where(t => t.IsPublic && t.Name == stem)];
            if (sources.Length != 1)
            {
                problems.Add($"{dto.Name}: {sources.Length} Application types named {stem}.");
                continue;
            }

            if (!Names(dto).SequenceEqual(Names(sources[0])))
            {
                problems.Add($"{dto.Name}: {string.Join(",", Names(dto))} != {string.Join(",", Names(sources[0]))}.");
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void WireEnums_HaveTheApplicationMemberNames()
    {
        Type[] wireEnums = [.. IpcAssembly.Reference.GetTypes()
            .Where(t => t.Namespace == "GapActivator.Ipc.Contracts" && t.IsEnum && t.Name != "OperatorsGroupMode")];
        wireEnums.Length.ShouldBe(12);

        foreach (Type wire in wireEnums)
        {
            Type[] sources = [.. ApplicationAssembly.Reference.GetTypes()
                .Concat(DomainAssembly.Reference.GetTypes())
                .Where(t => t.IsEnum && t.IsPublic && t.Name == wire.Name)];
            sources.Length.ShouldBe(1, wire.Name);
            Enum.GetNames(wire).ShouldBe(Enum.GetNames(sources[0]), wire.Name);
        }
    }

    [Fact]
    public void ErrorCodes_EveryRejectionCodeHasAnIpcCode()
    {
        string[] rejections = [.. typeof(RejectionCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];
        string[] ipc = [.. typeof(IpcErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

        rejections.ShouldNotBeEmpty();
        rejections.Except(ipc).ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_ThroughTheRouter_StoresEveryDefinitionField()
    {
        await using var host = new ApplicationTestHost();
        IpcRequestRouter router = RouterFor(host);

        ProcessSavedDto saved = await CallAsync(
            router, IpcMessages.RegisterProcess, new RegisterProcessRequest(Definition()), Operator);
        ProcessDetailsDto details = await CallAsync(
            router, IpcMessages.GetProcess, new GetProcessRequest(saved.Id), Caller.System);

        details.Summary.Id.ShouldBe(saved.Id);
        details.Summary.Name.ShouldBe("Nightly export");
        details.Summary.Kind.ShouldBe(Wire.ProcessKind.RunToCompletion);
        details.Description.ShouldBe("Exports data");
        details.Executable.ShouldBe(@"C:\Tools\sample.exe");
        details.Arguments.ShouldBe("\"a b\" c");
        details.WorkingDirectory.ShouldBe(@"C:\Tools");
        details.TimeLimit.ShouldBe(TimeSpan.FromMinutes(5));
        details.GracePeriod.ShouldBe(TimeSpan.FromSeconds(45));
        details.Environment.Select(v => (v.Name, v.Value, v.IsSecret)).ShouldBe(
            [("PLAIN", "visible", false), ("TOKEN", null, true)]);
        details.Schedules.Select(s => (s.Kind, s.At, s.Rule)).ShouldBe(
        [
            (Wire.ScheduleInputKind.OneTime, ApplicationTestHost.StartTime.AddDays(1), null),
            (Wire.ScheduleInputKind.Recurring, ApplicationTestHost.StartTime, "FREQ=HOURLY"),
        ]);
        details.SensitiveHidden.ShouldBeFalse();
        saved.ProcessesSharingExecutable.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunNow_ThroughTheRouter_ReturnsTheRunIdInTheResponse()
    {
        await using var host = new ApplicationTestHost();
        IpcRequestRouter router = RouterFor(host);
        ProcessSavedDto saved = await CallAsync(
            router, IpcMessages.RegisterProcess, new RegisterProcessRequest(Definition()), Operator);

        RunNowResponse response = await CallAsync(router, IpcMessages.RunNow, new RunNowRequest(saved.Id), Operator);

        host.Store.Runs.Keys.Select(k => k.Value).ShouldBe([response.RunId]);
    }

    // Final review I-3: a sub-daily rule with no match is seconds of CPU, so only two expansions run at once.
    [Fact]
    public async Task PreviewSchedule_ThirdConcurrentCall_WaitsForOneOfTheFirstTwo()
    {
        var queries = new GatedQueryDispatcher();
        IpcRequestRouter router = InertRouter(queries);
        Task Call() => CallAsync(
            router, IpcMessages.PreviewSchedule, new PreviewScheduleRequest("FREQ=HOURLY", ApplicationTestHost.StartTime, 1), Operator);

        Task first = Call();
        Task second = Call();
        Task third = Call();
        await queries.WaitForEntriesAsync(2).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        queries.Entered.ShouldBe(2);
        third.IsCompleted.ShouldBeFalse();

        queries.ReleaseOne();
        await queries.WaitForEntriesAsync(3).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queries.ReleaseAll();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PreviewSchedule_WaitingForASlot_EndsWhenTheStoppingTokenFires()
    {
        var queries = new GatedQueryDispatcher();
        IpcRequestRouter router = InertRouter(queries);
        Task First() => CallAsync(
            router, IpcMessages.PreviewSchedule, new PreviewScheduleRequest("FREQ=HOURLY", ApplicationTestHost.StartTime, 1), Operator);
        Task a = First();
        Task b = First();
        await queries.WaitForEntriesAsync(2).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var stopping = new CancellationTokenSource();

        Task<JsonElement> waiting = router.DispatchAsync(
            router.Find(IpcMessages.PreviewSchedule.Name),
            Element(new PreviewScheduleRequest("FREQ=HOURLY", ApplicationTestHost.StartTime, 1), IpcMessages.PreviewSchedule.RequestType),
            Operator,
            stopping.Token);
        await stopping.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        queries.ReleaseAll();
        await Task.WhenAll(a, b);
    }

    [Fact]
    public async Task GetProcess_AsViewer_HidesArgumentsAndEnvironmentValues()
    {
        await using var host = new ApplicationTestHost();
        IpcRequestRouter router = RouterFor(host);
        ProcessSavedDto saved = await CallAsync(
            router, IpcMessages.RegisterProcess, new RegisterProcessRequest(Definition()), Operator);

        ProcessDetailsDto details = await CallAsync(
            router, IpcMessages.GetProcess, new GetProcessRequest(saved.Id), Caller.Viewer("viewer"));

        details.Arguments.ShouldBeNull();
        details.Environment.Count.ShouldBe(2);
        details.Environment.ShouldAllBe(v => v.Value == null);
        details.SensitiveHidden.ShouldBeTrue();
    }

    [Fact]
    public async Task ListRuns_ThroughTheRouter_MapsTheWholeFilter()
    {
        var queries = new CapturingQueryDispatcher();
        IpcRequestRouter router = InertRouter(queries);
        var processId = Guid.NewGuid();
        DateTimeOffset from = ApplicationTestHost.StartTime;
        DateTimeOffset to = from.AddDays(2);
        var filter = new RunFilterDto(processId, [Wire.RunOutcome.Failed, Wire.RunOutcome.TimedOut], from, to, 3, 25);

        await CallAsync(router, IpcMessages.ListRuns, new ListRunsRequest(filter), Operator);

        var query = queries.Received.ShouldBeOfType<ListRuns>();
        query.Filter.ProcessId!.Value.Value.ShouldBe(processId);
        query.Filter.Outcomes.ShouldBe(
            [GapActivator.Domain.Runs.RunOutcome.Failed, GapActivator.Domain.Runs.RunOutcome.TimedOut]);
        query.Filter.From.ShouldBe(from);
        query.Filter.To.ShouldBe(to);
        query.Filter.Page.ShouldBe(3);
        query.Filter.PageSize.ShouldBe(25);
        queries.Caller.ShouldBe(Operator);
    }

    // Review Focus 2.
    [Theory]
    [InlineData("\"kind\":0")]
    [InlineData("\"kind\":\"Bogus\"")]
    public async Task Dispatch_EnumAsNumberOrUnknownName_IsInvalid(string replacement)
    {
        await using var host = new ApplicationTestHost();
        IpcRequestRouter router = RouterFor(host);
        ProcessDefinitionDto definition = Definition() with { Schedules = [] };
        string json = Element(new RegisterProcessRequest(definition), IpcJsonContext.Default.RegisterProcessRequest)
            .GetRawText()
            .Replace("\"kind\":\"RunToCompletion\"", replacement, StringComparison.Ordinal);
        json.ShouldContain(replacement);
        using JsonDocument document = JsonDocument.Parse(json);

        Exception error = await Should.ThrowAsync<Exception>(() => router.DispatchAsync(
            router.Find(IpcMessages.RegisterProcess.Name), document.RootElement.Clone(), Operator, CancellationToken.None));

        IpcError mapped = IpcErrorMapper.Map(error, IpcMessages.RegisterProcess.Name, false);
        mapped.Code.ShouldBe(IpcErrorCodes.Invalid);
        host.Store.Processes.ShouldBeEmpty();
    }

    [Fact]
    public void EnumMap_UndefinedValue_ThrowsInvalidPayload()
    {
        Should.Throw<IpcInvalidPayloadException>(
            () => EnumMap.To<Wire.ProcessKind>((GapActivator.Domain.Processes.ProcessKind)7));
        EnumMap.To<Wire.ProcessKind>(GapActivator.Domain.Processes.ProcessKind.Service).ShouldBe(Wire.ProcessKind.Service);
    }

    private static string[] Names(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name)];

    private static bool ImplementsOpen(Type type, Type open) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == open);

    private static bool ImplementsMessage(Type type) =>
        ImplementsOpen(type, typeof(ICommand<>)) || ImplementsOpen(type, typeof(IQuery<>));

    private sealed class NullCommandDispatcher : ICommandDispatcher
    {
        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, Caller caller, CancellationToken ct)
            where TCommand : ICommand<TResult> => throw new NotSupportedException();
    }

    private sealed class GatedQueryDispatcher : IQueryDispatcher
    {
        private readonly Lock _sync = new();
        private readonly List<TaskCompletionSource> _gates = [];
        private int _entered;
        private bool _all;

        public int Entered
        {
            get
            {
                lock (_sync)
                {
                    return _entered;
                }
            }
        }

        public async Task WaitForEntriesAsync(int count)
        {
            while (Entered < count)
            {
                await Task.Delay(10);
            }
        }

        public void ReleaseOne()
        {
            lock (_sync)
            {
                _gates.FirstOrDefault(g => !g.Task.IsCompleted)?.TrySetResult();
            }
        }

        public void ReleaseAll()
        {
            lock (_sync)
            {
                _all = true;
                _gates.ForEach(g => g.TrySetResult());
            }
        }

        public async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, Caller caller, CancellationToken ct)
            where TQuery : IQuery<TResult>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _entered++;
                _gates.Add(gate);
                if (_all)
                {
                    gate.TrySetResult();
                }
            }

            await gate.Task.WaitAsync(ct);
            object result = (IReadOnlyList<DateTimeOffset>)[];
            return (TResult)result;
        }
    }

    private sealed class CapturingQueryDispatcher : IQueryDispatcher
    {
        public object? Received { get; private set; }

        public Caller? Caller { get; private set; }

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, Caller caller, CancellationToken ct)
            where TQuery : IQuery<TResult>
        {
            Received = query;
            Caller = caller;
            object result = new RunRowPage([], 0);
            return Task.FromResult((TResult)result);
        }
    }
}
