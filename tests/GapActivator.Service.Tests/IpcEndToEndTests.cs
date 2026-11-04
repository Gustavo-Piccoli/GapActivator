using System.Diagnostics;
using GapActivator.Application.Cycle;
using GapActivator.Application.Messaging;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;

namespace GapActivator.Service.Tests;

/// <summary>
/// The real host, in-process, driven only through <see cref="GapActivatorClient"/> over a unique pipe (done-when
/// ruling 13). Every test resolves its callers with a <see cref="FakeCallerResolver"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IpcEndToEndTests
{
    private const string Password = "Pa55-w0rd-Synthetic";

    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RunWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pipe_OperatorRegistersAndRunsTheSample_SucceedsWithOutputAndAPush()
    {
        await using E2EFixture fixture = await E2EFixture.StartAsync(Ct);
        GapActivatorClient client = await fixture.ConnectAsync(ConnectWait, Ct);
        var pushes = new List<StateChangedDto>();
        client.StateChanged += (_, e) =>
        {
            lock (pushes)
            {
                pushes.Add(e);
            }
        };

        ProcessSavedDto saved = await client.RegisterProcessAsync(
            Definition("Sample via pipe", ChildPaths.SampleChild, "1", [new("SAMPLE_MODE", "fast", false)]), Ct);
        Guid id = saved.Id;
        Guid runId = await client.RunNowAsync(id, Ct);
        int pushesAtReturn = Count(pushes);

        RunRowDto run = await WaitForRunAsync(client, id, r => r.State == RunState.Completed);
        run.Id.ShouldBe(runId);
        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.Trigger.ShouldBe(RunTrigger.Manual);

        RunDetailsDto details = await client.GetRunAsync(runId, Ct);
        details.Output.Text.ShouldContain("Done.");
        details.Output.Text.ShouldContain($"Started by GapActivator (PID {Environment.ProcessId}).");

        // Id fidelity is Event_WithIds_PushesThoseIds; here only that a push arrived after RunNowAsync returned.
        Count(pushes).ShouldBeGreaterThan(pushesAtReturn);
    }

    [Fact]
    public async Task Pipe_Viewer_CannotRegisterButListsProcessesWithoutSensitiveValues()
    {
        await using E2EFixture fixture = await E2EFixture.StartAsync(Ct);
        GapActivatorClient operatorClient = await fixture.ConnectAsync(ConnectWait, Ct);
        Guid registered = (await operatorClient.RegisterProcessAsync(
            Definition("Sensitive via pipe", ChildPaths.SampleChild, "1", [new("SAMPLE_MODE", "fast", false)]), Ct)).Id;

        fixture.Callers.Next.Enqueue(Caller.Viewer(@"TEST\viewer"));
        GapActivatorClient viewer = await fixture.ConnectAsync(ConnectWait, Ct);
        viewer.Server.Caller.CanChange.ShouldBeFalse();

        GapActivatorRequestException rejected = await Should.ThrowAsync<GapActivatorRequestException>(
            () => viewer.RegisterProcessAsync(Definition("Not allowed", ChildPaths.SampleChild, "1", []), Ct));
        rejected.Code.ShouldBe(IpcErrorCodes.Unauthorized);
        rejected.Retryable.ShouldBeFalse();

        IReadOnlyList<ProcessSummaryDto> processes = await viewer.ListProcessesAsync(Ct);
        processes.Select(p => p.Name).ShouldBeSubsetOf(
            [E2EFixture.ScheduledName, E2EFixture.LongName, E2EFixture.StubbornName, "Sensitive via pipe"]);
        processes.Select(p => p.Name).ShouldContain(E2EFixture.ScheduledName);
        processes.Select(p => p.Name).ShouldContain(E2EFixture.LongName);
        processes.Select(p => p.Name).ShouldContain(E2EFixture.StubbornName);
        typeof(ProcessSummaryDto).GetProperty("Arguments").ShouldBeNull();
        typeof(ProcessSummaryDto).GetProperty("Environment").ShouldBeNull();

        ProcessDetailsDto details = await viewer.GetProcessAsync(registered, Ct);
        details.Arguments.ShouldBeNull();
        details.Environment[0].Value.ShouldBeNull();
        details.SensitiveHidden.ShouldBeTrue();
    }

    [Fact]
    public async Task Pipe_BeforeReconcileCompletes_NothingListens()
    {
        HandlerGate<ReconcileOnStartup, Unit>? gate = null;
        await using E2EFixture fixture = await E2EFixture.StartAsync(
            Ct,
            b => gate = HandlerGate<ReconcileOnStartup, Unit>.Install(b.Services),
            waitForReconcile: false);
        gate.ShouldNotBeNull();
        try
        {
            await Should.ThrowAsync<TimeoutException>(() => fixture.ConnectAsync(TimeSpan.FromMilliseconds(500), Ct));
        }
        finally
        {
            gate.Release();
        }

        GapActivatorClient client = await fixture.ConnectAsync(ConnectWait, Ct);
        client.Server.Caller.CanChange.ShouldBeTrue();
    }

    [Fact]
    public async Task Pipe_SendTestEmailAuthFailure_ReturnsEmailTestFailedWithoutThePasswordOrItsProtectedValue()
    {
        await using E2EFixture fixture = await E2EFixture.StartAsync(Ct);
        await using var smtp = new MiniSmtpServer
        {
            AdvertiseAuthPlain = true,
            RejectAuthWith = "535 5.7.8 Authentication credentials invalid",
        };
        GapActivatorClient client = await fixture.ConnectAsync(ConnectWait, Ct);

        SettingsViewDto current = await client.GetSettingsAsync(Ct);
        EmailInputDto Email(SecretChangeKind kind) => new(
            true,
            "127.0.0.1",
            smtp.Port,
            TlsMode.None,
            "user",
            new SecretChangeDto(kind, kind == SecretChangeKind.Set ? Password : null),
            "sender@example.com",
            ["recipient@example.com"],
            [AlertKind.ProcessBlocked]);

        await client.UpdateSettingsAsync(
            new SettingsInputDto(
                current.CycleInterval,
                current.FailureLimit,
                current.DefaultTimeLimit,
                current.DefaultGracePeriod,
                current.LogRetentionDays,
                current.HistoryRetentionDays,
                current.LogLevel,
                current.CheckForUpdates,
                Email(SecretChangeKind.Set)),
            Ct);
        await using E2ESeeding seeding = E2ESeeding.Open(fixture.DataRoot);
        string protectedValue = (await seeding.StoredEmailPasswordAsync(Ct)).ShouldNotBeNull();

        foreach (SecretChangeKind kind in new[] { SecretChangeKind.Keep, SecretChangeKind.Set })
        {
            GapActivatorRequestException failure = await Should.ThrowAsync<GapActivatorRequestException>(
                () => client.SendTestEmailAsync(Email(kind), Ct));
            failure.Code.ShouldBe(IpcErrorCodes.EmailTestFailed);
            failure.Retryable.ShouldBeFalse();
            failure.Message.ShouldNotContain(Password);
            failure.Message.ShouldNotContain(protectedValue);
            failure.Message.ShouldNotContain("dpapi:");
        }
    }

    private static ProcessDefinitionDto Definition(
        string name, string executable, string arguments, IReadOnlyList<EnvironmentVariableInputDto> environment) =>
        new(name, "", ProcessKind.RunToCompletion, executable, arguments, null, environment, null, null, []);

    private static int Count(List<StateChangedDto> pushes)
    {
        lock (pushes)
        {
            return pushes.Count;
        }
    }

    private static async Task<RunRowDto> WaitForRunAsync(GapActivatorClient client, Guid processId, Func<RunRowDto, bool> predicate)
    {
        var filter = new RunFilterDto(processId, null, null, null, 1, 200);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            RunRowPageDto page = await client.ListRunsAsync(filter, Ct);
            if (page.Items.FirstOrDefault(predicate) is { } match)
            {
                return match;
            }

            if (watch.Elapsed > RunWait)
            {
                throw new TimeoutException($"No run matched within {RunWait}. Runs: [{string.Join("; ", page.Items)}]");
            }

            await Task.Delay(PollInterval, Ct);
        }
    }
}
