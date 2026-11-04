using System.Diagnostics;
using GapActivator.Ipc.Contracts;
using GapActivator.Ipc.Tests.Support;

namespace GapActivator.Ipc.Tests;

[Trait("Category", "Integration")]
public class GapActivatorClientTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static Task<GapActivatorClient> ConnectAsync(ScriptedPipeServer server, Func<string, ServiceLookup>? lookup = null) =>
        GapActivatorClient.ConnectAsync(new GapActivatorClientOptions
        {
            PipeName = server.PipeName,
            ServiceName = "GapActivator.Tests.NotInstalled",
            ConnectTimeout = Wait,
            LookupService = lookup ?? (_ => new ServiceLookup.NotInstalled()),
        });

    [Fact]
    public async Task Connect_NothingListening_ThrowsTimeoutWithinTheTimeout()
    {
        var options = new GapActivatorClientOptions
        {
            PipeName = $"GapActivator.Tests.{Guid.NewGuid():N}",
            ConnectTimeout = TimeSpan.FromMilliseconds(300),
            LookupService = _ => new ServiceLookup.NotInstalled(),
        };
        var watch = Stopwatch.StartNew();

        await Should.ThrowAsync<TimeoutException>(() => GapActivatorClient.ConnectAsync(options));

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Connect_Handshake_SendsHelloVersion1AndExposesTheReply()
    {
        await using var server = new ScriptedPipeServer();

        await using GapActivatorClient client = await ConnectAsync(server);

        await server.WaitForRequestsAsync(1);
        RequestEnvelope hello = server.Received[0];
        hello.Id.ShouldBe(1);
        hello.Type.ShouldBe("Hello");
        IpcCodec.ReadPayload(hello.Payload, IpcJsonContext.Default.HelloRequest).ProtocolVersion.ShouldBe(1);
        client.Server.ShouldBe(ScriptedPipeServer.DefaultHello);
    }

    [Fact]
    public async Task Connect_ServerReportsProtocolMismatch_ThrowsThatCode()
    {
        await using var server = new ScriptedPipeServer(
            helloReply: ServerEnvelope.Failure(1, new IpcError(IpcErrorCodes.ProtocolMismatch, "Version 1 only.", false)));

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => ConnectAsync(server));

        ex.Code.ShouldBe("protocol-mismatch");
        ex.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task Connect_ServerAnswersAnotherProtocolVersion_ThrowsProtocolMismatch()
    {
        HelloResponse reply = ScriptedPipeServer.DefaultHello with { ProtocolVersion = 2 };
        await using var server = new ScriptedPipeServer(
            helloReply: ServerEnvelope.Response(1, IpcCodec.ToElement(reply, IpcJsonContext.Default.HelloResponse)));

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => ConnectAsync(server));

        ex.Code.ShouldBe("protocol-mismatch");
    }

    // Pre-flight C3: the first server frame completes the handshake whatever its id.
    [Fact]
    public async Task Connect_ServerReportsBusyWithIdZero_ThrowsRetryableBusy()
    {
        await using var server = new ScriptedPipeServer(
            helloReply: ServerEnvelope.Failure(0, new IpcError(IpcErrorCodes.Busy, "Too many connections.", true)));

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => ConnectAsync(server));

        ex.Code.ShouldBe("busy");
        ex.Retryable.ShouldBeTrue();
    }

    [Fact]
    public async Task Connect_ServiceNotInstalled_ServerVerifiedIsFalse()
    {
        await using var server = new ScriptedPipeServer();

        await using GapActivatorClient client = await ConnectAsync(server, _ => new ServiceLookup.NotInstalled());

        client.ServerVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task Connect_InstalledServiceWithAnotherPid_ThrowsServerNotVerifiedBeforeWritingAnything()
    {
        await using var server = new ScriptedPipeServer();

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(
            () => ConnectAsync(server, _ => new ServiceLookup.Running((uint)Environment.ProcessId + 1)));

        ex.Code.ShouldBe("server-not-verified");
        ex.Retryable.ShouldBeFalse();
        await server.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);
        server.Received.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Connect_InstalledServiceWithTheServerPid_ServerVerifiedIsTrue()
    {
        await using var server = new ScriptedPipeServer();

        await using GapActivatorClient client = await ConnectAsync(server, _ => new ServiceLookup.Running((uint)Environment.ProcessId));

        client.ServerVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task Send_ResponsesOutOfOrder_AreMatchedById()
    {
        Guid firstAnswer = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid secondAnswer = Guid.Parse("22222222-2222-2222-2222-222222222222");
        RequestEnvelope? first = null;
        await using var server = new ScriptedPipeServer(async (s, request) =>
        {
            if (first is null)
            {
                first = request;
                return;
            }

            await s.RespondAsync(request.Id, new RunNowResponse(secondAnswer), IpcJsonContext.Default.RunNowResponse);
            await s.RespondAsync(first.Id, new RunNowResponse(firstAnswer), IpcJsonContext.Default.RunNowResponse);
        });
        await using GapActivatorClient client = await ConnectAsync(server);

        Task<Guid> one = client.RunNowAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        await server.WaitForRequestsAsync(2);
        Task<Guid> two = client.RunNowAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        (await one.WaitAsync(Wait, TestContext.Current.CancellationToken)).ShouldBe(firstAnswer);
        (await two.WaitAsync(Wait, TestContext.Current.CancellationToken)).ShouldBe(secondAnswer);
    }

    [Fact]
    public async Task Send_ErrorResponse_ThrowsWithCodeMessageAndRetryable()
    {
        await using var server = new ScriptedPipeServer((s, request) =>
            s.FailAsync(request.Id, new IpcError("process-blocked", "Blocked.", true)));
        await using GapActivatorClient client = await ConnectAsync(server);

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => client.EnableProcessAsync(Guid.NewGuid()));

        ex.Code.ShouldBe("process-blocked");
        ex.Message.ShouldBe("Blocked.");
        ex.Retryable.ShouldBeTrue();
    }

    [Fact]
    public async Task Send_Success_ReadsTheTypedPayload()
    {
        await using var server = new ScriptedPipeServer((s, request) =>
            s.RespondAsync(request.Id, new ListProcessesResponse([]), IpcJsonContext.Default.ListProcessesResponse));
        await using GapActivatorClient client = await ConnectAsync(server);

        IReadOnlyList<ProcessSummaryDto> items = await client.ListProcessesAsync(TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);

        items.ShouldBeEmpty();
        server.Received[1].Type.ShouldBe("ListProcesses");
        server.Received[1].Id.ShouldBe(2);
    }

    [Fact]
    public async Task Send_Cancelled_ThrowsOperationCanceledAndIgnoresTheLateResponse()
    {
        await using var server = new ScriptedPipeServer(async (s, request) =>
        {
            if (request.Type == "RunNow")
            {
                return; // not answered in time
            }

            await s.RespondAsync(request.Id, new ListProcessesResponse([]), IpcJsonContext.Default.ListProcessesResponse);
        });
        await using GapActivatorClient client = await ConnectAsync(server);
        using var cts = new CancellationTokenSource();

        Task<Guid> call = client.RunNowAsync(Guid.NewGuid(), cts.Token);
        await server.WaitForRequestsAsync(2);
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => call);

        // The late response is dropped and the connection keeps working.
        await server.RespondAsync(server.Received[1].Id, new RunNowResponse(Guid.NewGuid()), IpcJsonContext.Default.RunNowResponse);
        (await client.ListProcessesAsync(TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Pushes_RaiseNotificationReceivedAndStateChanged()
    {
        await using var server = new ScriptedPipeServer();
        await using GapActivatorClient client = await ConnectAsync(server);
        var notification = new TaskCompletionSource<NotificationDto>();
        var state = new TaskCompletionSource<StateChangedDto>();
        client.NotificationReceived += (_, e) => notification.TrySetResult(e);
        client.StateChanged += (_, e) => state.TrySetResult(e);
        Guid processId = Guid.NewGuid();

        await server.PushAsync(
            IpcEvents.Notification,
            new NotificationDto(NotificationSeverity.Warning, "Title", "Message", processId, null),
            IpcJsonContext.Default.NotificationDto);
        await server.PushAsync(IpcEvents.StateChanged, new StateChangedDto(processId, null), IpcJsonContext.Default.StateChangedDto);

        NotificationDto received = await notification.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        received.Title.ShouldBe("Title");
        received.Severity.ShouldBe(NotificationSeverity.Warning);
        (await state.Task.WaitAsync(Wait, TestContext.Current.CancellationToken)).ProcessId.ShouldBe(processId);
    }

    // Review Focus 5
    [Fact]
    public async Task ServerCloses_WithPendingRequest_FailsItDisconnectedAndRaisesDisconnectedOnce()
    {
        await using var server = new ScriptedPipeServer((_, _) => Task.CompletedTask);
        await using GapActivatorClient client = await ConnectAsync(server);
        int raised = 0;
        client.Disconnected += (_, _) => Interlocked.Increment(ref raised);

        Task<Guid> pending = client.RunNowAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        await server.WaitForRequestsAsync(2);
        server.Close();

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        ex.Code.ShouldBe("disconnected");
        ex.Retryable.ShouldBeTrue();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        raised.ShouldBe(1);
        await client.DisposeAsync();
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task ServerSendsOversizedFrame_ClientDisconnects()
    {
        await using var server = new ScriptedPipeServer();
        await using GapActivatorClient client = await ConnectAsync(server);
        var disconnected = new TaskCompletionSource<DisconnectedEventArgs>();
        client.Disconnected += (_, e) => disconnected.TrySetResult(e);

        await server.SendRawAsync(ScriptedPipeServer.OversizedHeader());

        DisconnectedEventArgs args = await disconnected.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        args.Error.ShouldBeOfType<IpcProtocolException>();
        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => client.GetDashboardAsync());
        ex.Code.ShouldBe("disconnected");
    }

    [Fact]
    public async Task Dispose_RaisesDisconnectedOnceAndLaterSendsFailDisconnected()
    {
        await using var server = new ScriptedPipeServer();
        GapActivatorClient client = await ConnectAsync(server);
        int raised = 0;
        client.Disconnected += (_, _) => Interlocked.Increment(ref raised);

        await client.DisposeAsync();
        await client.DisposeAsync();

        raised.ShouldBe(1);
        var ex = await Should.ThrowAsync<GapActivatorRequestException>(() => client.PauseInstallationAsync());
        ex.Code.ShouldBe("disconnected");
        ex.Retryable.ShouldBeTrue();
    }
}
