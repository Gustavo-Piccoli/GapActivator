using GapActivator.Application.Messaging;
using GapActivator.Application.Settings;
using GapActivator.Domain.Runs;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using GapActivator.Service.Ipc;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DomainRunState = GapActivator.Domain.Runs.RunState;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Wire = GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Tests.Ipc;

[Trait("Category", "Integration")]
public sealed class IpcServerTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

    private static ProcessDefinitionDto Definition() => new(
        "Nightly export",
        "Exports data",
        Wire.ProcessKind.RunToCompletion,
        @"C:\Tools\sample.exe",
        string.Empty,
        @"C:\Tools",
        [],
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(45),
        [new ScheduleInputDto(null, Wire.ScheduleInputKind.OneTime, ApplicationTestHost.StartTime.AddDays(1), null)]);

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(timeout, TestContext.Current.CancellationToken));
        return finished == task;
    }

    private static async Task<bool> BecomesTrueAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return condition();
            }
        }

        return true;
    }

    [Fact]
    public async Task Ready_NotYetCompleted_NoPipeUntilItCompletes()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using IpcTestServer server = await IpcTestServer.StartAsync(ready: ready.Task);

        await Should.ThrowAsync<TimeoutException>(() => server.ConnectAsync(TimeSpan.FromMilliseconds(500)));

        ready.TrySetResult();
        await using GapActivatorClient client = await server.ConnectAsync(Long).WaitAsync(Long, TestContext.Current.CancellationToken);
        client.Server.ProtocolVersion.ShouldBe(1);
    }

    [Fact]
    public async Task Ready_Faulted_NeverCreatesThePipeAndReturns()
    {
        var ready = new TaskCompletionSource();
        ready.SetException(new InvalidOperationException("reconcile failed"));
        await using IpcTestServer server = await IpcTestServer.StartAsync(ready: ready.Task);

        await server.Running.WaitAsync(Short, TestContext.Current.CancellationToken);
        await Should.ThrowAsync<TimeoutException>(() => server.ConnectAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Hello_Operator_ReturnsVersionCallerAndOperatorsMode()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();

        await using GapActivatorClient client = await server.ConnectAsync();

        client.Server.ProtocolVersion.ShouldBe(1);
        client.Server.ServiceVersion.ShouldBe(ServiceInfo.Version);
        client.Server.Caller.ShouldBe(new CallerDto(@"TEST\operator", true));
        client.Server.OperatorsGroupMode.ShouldBe(OperatorsGroupMode.Enabled);
    }

    [Fact]
    public async Task Hello_OperatorsGroupDisabled_IsReported()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        server.Callers.OperatorsGroupEnabled = false;

        await using GapActivatorClient client = await server.ConnectAsync();

        client.Server.OperatorsGroupMode.ShouldBe(OperatorsGroupMode.Disabled);
    }

    [Theory]
    [InlineData(true, LogLevel.Warning)]
    [InlineData(false, LogLevel.Error)]
    public async Task Hello_ResolverThrows_IsUnauthorizedClosesAndLogsWithoutLeaking(bool notIdentifiable, LogLevel expected)
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        server.Callers.Failure = notIdentifiable
            ? new UnauthorizedAccessException("secret detail")
            : new InvalidOperationException("secret detail");
        await using RawPipeClient raw = await server.ConnectRawAsync();

        await raw.SendHelloAsync();

        ServerEnvelope? reply = await raw.ReadAsync(Long);
        reply.ShouldNotBeNull();
        reply.Ok.ShouldBe(false);
        reply.Error!.Code.ShouldBe(IpcErrorCodes.Unauthorized);
        reply.Error.Message.ShouldBe("The client could not be identified.");
        reply.Error.Message.ShouldNotContain("secret detail");
        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
        LogEntry entry = server.Host.Log.Entries.Single(e => e.Category == "IpcConnection" && e.Level >= LogLevel.Warning);
        entry.Level.ShouldBe(expected);
        entry.Message.ShouldNotContain("secret detail");
        (entry.Exception is null).ShouldBe(notIdentifiable);
    }

    // Final review I-1: client text is logged, and the log is line-oriented.
    [Fact]
    public async Task Hello_ClientVersionWithLineBreaks_LogsOneSanitizedTruncatedLine()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();

        await raw.SendHelloAsync(clientVersionJson: @"1.0)\r\n2026-11-04 10:00:00.000 -03:00 [ERR] forged" + new string('x', 200));
        (await raw.ReadAsync(Long)).ShouldNotBeNull();

        LogEntry entry = server.Host.Log.Entries.Single(e => e.Message.Contains("connected (client", StringComparison.Ordinal));
        entry.Message.ShouldNotContain("\r");
        entry.Message.ShouldNotContain("\n");
        entry.Message.ShouldContain("1.0)  2026-11-04");
        entry.Message.ShouldNotContain("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
    }

    [Fact]
    public async Task MalformedEnvelopeWithLineBreaksInAPropertyName_LogsASingleLine()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendTextAsync(@"{""id"":2,""type"":""GetDashboard"",""x\r\n2026-11-04 10:00:00.000 -03:00 [ERR] FAKE"":}");

        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
        (await BecomesTrueAsync(() => server.Host.Log.Entries.Any(e => e.Message.Contains("broke the protocol")), Short)).ShouldBeTrue();
        server.Host.Log.Entries.ShouldAllBe(e => !e.Message.Contains('\r') && !e.Message.Contains('\n'));
    }

    [Fact]
    public async Task UnknownTypeWithLineBreaks_IsLoggedAndRepliedAsASingleLine()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendRequestAsync(2, @"Evil\r\n2026-11-04 10:00:00.000 -03:00 [ERR] FAKE");
        ServerEnvelope? reply = await raw.ReadAsync(Long);

        reply.ShouldNotBeNull();
        reply.Error!.Code.ShouldBe(IpcErrorCodes.UnknownMessage);
        reply.Error.Message.ShouldNotContain("\n");
        (await BecomesTrueAsync(() => server.Host.Log.Entries.Any(e => e.Message.Contains("was written")), Short)).ShouldBeTrue();
        server.Host.Log.Entries.ShouldAllBe(e => !e.Message.Contains('\r') && !e.Message.Contains('\n'));
    }

    [Fact]
    public async Task Hello_WrongProtocolVersion_ReturnsProtocolMismatchAndCloses()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();

        await raw.SendHelloAsync(protocolVersion: 2);

        ServerEnvelope? reply = await raw.ReadAsync(Long);
        reply.ShouldNotBeNull();
        reply.Ok.ShouldBe(false);
        reply.Error!.Code.ShouldBe(IpcErrorCodes.ProtocolMismatch);
        reply.Error.Message.ShouldBe("The service speaks protocol version 1; the client sent 2.");
        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
    }

    [Fact]
    public async Task FirstMessageNotHello_ReturnsProtocolMismatchAndCloses()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();

        await raw.SendRequestAsync(7, "GetDashboard");

        ServerEnvelope? reply = await raw.ReadAsync(Long);
        reply.ShouldNotBeNull();
        reply.Id.ShouldBe(7);
        reply.Error!.Code.ShouldBe(IpcErrorCodes.ProtocolMismatch);
        reply.Error.Message.ShouldBe("The first message must be Hello.");
        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
    }

    // Review Focus 1: a client that never says Hello must not hold a slot.
    [Fact]
    public async Task Hello_NotSentWithinTheTimeout_ClosesAndFreesTheSlot()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync(
            options: o => o with { HelloTimeout = TimeSpan.FromSeconds(1) });
        await using RawPipeClient raw = await server.ConnectRawAsync();

        (await raw.WaitForEndAsync(TimeSpan.FromSeconds(3))).ShouldBeTrue();

        (await BecomesTrueAsync(() => server.Registry.Count == 0, Short)).ShouldBeTrue();
    }

    [Fact]
    public async Task SeventeenthConnection_IsRefusedWithRetryableBusy()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        var clients = new List<GapActivatorClient>();
        try
        {
            for (int i = 0; i < 16; i++)
            {
                clients.Add(await server.ConnectAsync());
            }

            GapActivatorRequestException busy =
                await Should.ThrowAsync<GapActivatorRequestException>(() => server.ConnectAsync());
            busy.Code.ShouldBe(IpcErrorCodes.Busy);
            busy.Retryable.ShouldBeTrue();

            await clients[0].DisposeAsync();
            clients.RemoveAt(0);

            using var deadline = new CancellationTokenSource(Long);
            GapActivatorClient? replacement = null;
            while (replacement is null)
            {
                try
                {
                    replacement = await server.ConnectAsync();
                }
                catch (GapActivatorRequestException ex) when (ex.Code == IpcErrorCodes.Busy && !deadline.IsCancellationRequested)
                {
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }
            }

            clients.Add(replacement);
        }
        finally
        {
            foreach (GapActivatorClient client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Viewer_RegisterProcess_IsUnauthorizedAndQueriesWork()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        server.Callers.Default = Caller.Viewer(@"TEST\viewer");
        await using GapActivatorClient client = await server.ConnectAsync();

        GapActivatorRequestException ex =
            await Should.ThrowAsync<GapActivatorRequestException>(() => client.RegisterProcessAsync(Definition()));

        ex.Code.ShouldBe(IpcErrorCodes.Unauthorized);
        ex.Retryable.ShouldBeFalse();
        (await client.GetDashboardAsync(TestContext.Current.CancellationToken)).IsPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task InternalCommandName_IsUnknownMessageAndTheConnectionStaysOpen()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendRequestAsync(2, "StopRun");
        ServerEnvelope? unknown = await raw.ReadAsync(Long);
        await raw.SendRequestAsync(3, "GetDashboard");
        ServerEnvelope? dashboard = await raw.ReadAsync(Long);

        unknown.ShouldNotBeNull();
        unknown.Id.ShouldBe(2);
        unknown.Error!.Code.ShouldBe(IpcErrorCodes.UnknownMessage);
        dashboard.ShouldNotBeNull();
        dashboard.Id.ShouldBe(3);
        dashboard.Ok.ShouldBe(true);
    }

    [Fact]
    public async Task SecondHello_IsInvalidAndTheConnectionStaysOpen()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendHelloAsync(id: 2);
        ServerEnvelope? reply = await raw.ReadAsync(Long);
        await raw.SendRequestAsync(3, "GetDashboard");
        ServerEnvelope? dashboard = await raw.ReadAsync(Long);

        reply.ShouldNotBeNull();
        reply.Id.ShouldBe(2);
        reply.Error!.Code.ShouldBe(IpcErrorCodes.Invalid);
        reply.Error.Message.ShouldBe("Hello was already received.");
        dashboard.ShouldNotBeNull();
        dashboard.Ok.ShouldBe(true);
    }

    [Fact]
    public async Task MalformedPayload_IsInvalidAndTheConnectionStaysOpen()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendRequestAsync(2, "RunNow", """{"id":"x"}""");
        ServerEnvelope? invalid = await raw.ReadAsync(Long);
        await raw.SendRequestAsync(3, "GetDashboard");
        ServerEnvelope? dashboard = await raw.ReadAsync(Long);

        invalid.ShouldNotBeNull();
        invalid.Error!.Code.ShouldBe(IpcErrorCodes.Invalid);
        dashboard.ShouldNotBeNull();
        dashboard.Ok.ShouldBe(true);
    }

    [Fact]
    public async Task MalformedEnvelope_ClosesTheConnection()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendTextAsync("not json");

        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
    }

    [Fact]
    public async Task OversizedFrameHeader_ClosesTheConnection()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();
        await raw.HelloAsync();

        await raw.SendHeaderAsync((16 * 1024 * 1024) + 1);

        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
    }

    // Final review I-2: nothing is allocated for a frame that claims more than a Hello can need.
    [Fact]
    public async Task OversizedFrameHeaderBeforeHello_ClosesTheConnection()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using RawPipeClient raw = await server.ConnectRawAsync();

        await raw.SendHeaderAsync(64 * 1024 + 1);

        (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
    }

    [Fact]
    public async Task OversizedFrameHeaderOnTheBusyPath_ClosesTheConnection()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        var clients = new List<GapActivatorClient>();
        try
        {
            for (int i = 0; i < 16; i++)
            {
                clients.Add(await server.ConnectAsync());
            }

            await using RawPipeClient raw = await server.ConnectRawAsync();
            await raw.SendHeaderAsync(16 * 1024 * 1024);

            (await raw.WaitForEndAsync(Short)).ShouldBeTrue();
        }
        finally
        {
            foreach (GapActivatorClient client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Rejection_MapsItsCodeNotRetryable()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();

        GapActivatorRequestException ex =
            await Should.ThrowAsync<GapActivatorRequestException>(() => client.RunNowAsync(Guid.NewGuid()));

        ex.Code.ShouldBe(IpcErrorCodes.NotFound);
        ex.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task ConcurrencyConflict_MapsToRetryableConflict()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        server.Host.Services.GetRequiredService<ScriptedCommandDispatcher>()
            .FailNext<PauseInstallation>(1, new DbUpdateConcurrencyException("x"));
        await using GapActivatorClient client = await server.ConnectAsync();

        GapActivatorRequestException ex =
            await Should.ThrowAsync<GapActivatorRequestException>(() => client.PauseInstallationAsync(TestContext.Current.CancellationToken));

        ex.Code.ShouldBe(IpcErrorCodes.Conflict);
        ex.Retryable.ShouldBeTrue();
    }

    [Fact]
    public async Task UnexpectedException_MapsToInternalAndIsLoggedAtError()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        server.Host.Services.GetRequiredService<ScriptedCommandDispatcher>()
            .FailNext<PauseInstallation>(1, new InvalidOperationException("secret detail"));
        await using GapActivatorClient client = await server.ConnectAsync();

        GapActivatorRequestException ex =
            await Should.ThrowAsync<GapActivatorRequestException>(() => client.PauseInstallationAsync(TestContext.Current.CancellationToken));

        ex.Code.ShouldBe(IpcErrorCodes.Internal);
        ex.Message.ShouldNotContain("secret detail");
        server.Host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Category == "IpcConnection");
    }

    [Fact]
    public async Task SlowCommand_DoesNotBlockAQueryOnTheSameConnection()
    {
        var handler = new BlockingCommandHandler<PauseInstallation>();
        await using IpcTestServer server = await IpcTestServer.StartAsync(
            s => s.AddScoped<ICommandHandler<PauseInstallation, Unit>>(_ => handler));
        await using GapActivatorClient client = await server.ConnectAsync();

        Task slow = client.PauseInstallationAsync(TestContext.Current.CancellationToken);
        await handler.Reached.WaitAsync(Long, TestContext.Current.CancellationToken);

        await client.GetDashboardAsync(TestContext.Current.CancellationToken).WaitAsync(Short, TestContext.Current.CancellationToken);

        handler.Release();
        await slow.WaitAsync(Long, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NinthConcurrentRequest_WaitsForAFreeSlot()
    {
        var handler = new BlockingCommandHandler<PauseInstallation>();
        await using IpcTestServer server = await IpcTestServer.StartAsync(
            s => s.AddScoped<ICommandHandler<PauseInstallation, Unit>>(_ => handler));
        await using GapActivatorClient client = await server.ConnectAsync();
        var dispatcher = server.Host.Services.GetRequiredService<ScriptedCommandDispatcher>();

        List<Task> pauses = [.. Enumerable.Range(0, 8).Select(_ => client.PauseInstallationAsync(TestContext.Current.CancellationToken))];
        await dispatcher.WaitForAttemptsAsync<PauseInstallation>(8).WaitAsync(Long, TestContext.Current.CancellationToken);

        Task query = client.GetDashboardAsync(TestContext.Current.CancellationToken);
        (await CompletesWithinAsync(query, TimeSpan.FromMilliseconds(500))).ShouldBeFalse();

        handler.Release();
        await Task.WhenAll([.. pauses, query]).WaitAsync(Long, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ManyConcurrentRequests_AreAllAnsweredWithTheirOwnResults()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        bool disconnected = false;
        client.Disconnected += (_, _) => disconnected = true;

        IReadOnlyList<DateTimeOffset>[] results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            client.PreviewScheduleAsync("FREQ=HOURLY", ApplicationTestHost.StartTime, (i % 50) + 1))).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        for (int i = 0; i < 100; i++)
        {
            results[i].Count.ShouldBe((i % 50) + 1);
        }

        disconnected.ShouldBeFalse();
    }

    // Review Focus 3: JSON escaping can multiply the size of a tail; it must still fit one frame.
    [Fact]
    public async Task GetRun_OneMebibyteOfControlCharactersQuotesAndNonAscii_RoundTripsUnderTheFrameLimit()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        Run run = new RunBuilder().InState(DomainRunState.Completed).Build();
        server.Host.Store.Runs[run.Id] = run;
        char[] cycle = ['\u0001', '"', '\\', 'ç', '密'];
        string text = string.Create(1_048_576, cycle, (span, c) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = c[i % c.Length];
            }
        });
        server.Host.Services.GetRequiredService<FakeRunOutputReader>().Outputs[run.Id] = new Application.Ports.RunOutput(true, text, false);
        await using GapActivatorClient client = await server.ConnectAsync();

        RunDetailsDto details = await client.GetRunAsync(run.Id.Value, TestContext.Current.CancellationToken);

        details.Output.Text.ShouldBe(text);
    }

    // Review Focus 4: a disconnect never cancels a running command.
    [Fact]
    public async Task ClientDisconnectsDuringACommand_CommandCompletesAndTheServerKeepsServing()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        var dispatcher = server.Host.Services.GetRequiredService<ScriptedCommandDispatcher>();
        CommandHold hold = dispatcher.HoldBefore<PauseInstallation>();
        GapActivatorClient client = await server.ConnectAsync();
        Task pending = client.PauseInstallationAsync(TestContext.Current.CancellationToken);
        await hold.Reached.WaitAsync(Long, TestContext.Current.CancellationToken);

        await client.DisposeAsync();
        await Should.ThrowAsync<GapActivatorRequestException>(() => pending);
        hold.Release();

        await dispatcher.WaitForHandledAsync<PauseInstallation>(1).WaitAsync(Long, TestContext.Current.CancellationToken);
        (await BecomesTrueAsync(() => server.Registry.Count == 0, Long)).ShouldBeTrue();
        await using GapActivatorClient second = await server.ConnectAsync();
        (await second.GetDashboardAsync(TestContext.Current.CancellationToken)).ShouldNotBeNull();

        // The write gate is free again: another gated command goes through at once.
        await server.Host.Send<ResumeInstallation, Unit>(new ResumeInstallation(), Caller.Operator("x"), ct: TestContext.Current.CancellationToken)
            .WaitAsync(Short, TestContext.Current.CancellationToken);
        server.Host.Log.Entries.ShouldNotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Shutdown_InFlightCommandGetsServiceStoppingThenTheConnectionCloses()
    {
        var handler = new BlockingCommandHandler<PauseInstallation>();
        await using IpcTestServer server = await IpcTestServer.StartAsync(
            s => s.AddScoped<ICommandHandler<PauseInstallation, Unit>>(_ => handler));
        await using GapActivatorClient client = await server.ConnectAsync();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => disconnected.TrySetResult();
        Task pending = client.PauseInstallationAsync(TestContext.Current.CancellationToken);
        await handler.Reached.WaitAsync(Long, TestContext.Current.CancellationToken);

        await server.StopAsync();

        GapActivatorRequestException ex = await Should.ThrowAsync<GapActivatorRequestException>(() => pending);
        ex.Code.ShouldBe(IpcErrorCodes.ServiceStopping);
        ex.Retryable.ShouldBeTrue();
        await disconnected.Task.WaitAsync(Long, TestContext.Current.CancellationToken);
        await server.Running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Shutdown_StopsAcceptingConnections()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using (await server.ConnectAsync())
        {
        }

        await server.StopAsync();

        await Should.ThrowAsync<TimeoutException>(() => server.ConnectAsync(TimeSpan.FromMilliseconds(500)));
    }
}
