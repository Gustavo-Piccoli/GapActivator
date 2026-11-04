using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Processes;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Application.Tests.Runs;

public class RunRecordingTests
{
    private const string Exe = RunTestHost.Executable;
    private const string SecretValue = "s3cret-value";

    // Review Focus 1
    [Fact]
    public async Task StopRun_AfterRunCompleted_IsNoOp()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await host.EndOf(run.Id);

        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.StopReason.ShouldBeNull();
        child.SignalCancelCount.ShouldBe(0);
        host.Store.History.ShouldNotContain(h => h.Kind == HistoryEntryKind.RunStopRequested);
    }

    [Fact]
    public async Task StopRun_UnknownRun_IsNoOp()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        await host.Run<StopRun, Unit>(new StopRun(RunId.New(), StopReason.UserCancel));

        host.Store.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task StopRun_Active_RequestsStopAndSignalsTheChild()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: TimeSpan.FromSeconds(5));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.Shutdown));
        await host.Time.WaitForTimersAsync(1).Within();

        run.State.ShouldBe(RunState.Stopping);
        run.StopReason.ShouldBe(StopReason.Shutdown);
        child.SignalCancelCount.ShouldBe(1);
        host.Store.History.ShouldContain(h => h.Kind == HistoryEntryKind.RunStopRequested && h.RunId == run.Id);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Interrupted);
    }

    [Fact]
    public async Task StopRun_FromOperator_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<StopRun, Unit>(
            new StopRun(RunId.New(), StopReason.UserCancel), Caller.Operator("alice")));

        error.Code.ShouldBe(RejectionCodes.Unauthorized);
    }

    [Fact]
    public async Task RecordRunLaunched_UnknownRun_LoggedAndReturns()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        await host.Run<RecordRunLaunched, Unit>(new RecordRunLaunched(RunId.New(), 1, host.Time.GetUtcNow()));

        host.LoggedAt(MsLogLevel.Warning, "RecordRunLaunchedHandler").ShouldBeTrue();
    }

    [Theory]
    [InlineData(RunState.Running)]
    [InlineData(RunState.Stopping)]
    [InlineData(RunState.Completed)]
    public async Task RecordRunLaunched_AlreadyPastLaunching_LoggedAndUnchanged(RunState state)
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(state).Build();
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordRunLaunched, Unit>(new RecordRunLaunched(run.Id, 99, host.Time.GetUtcNow()));

        run.State.ShouldBe(state);
        run.Pid.ShouldBe(4242);
        host.LoggedAt(MsLogLevel.Warning, "RecordRunLaunchedHandler").ShouldBeTrue();
    }

    [Fact]
    public async Task RecordRunLaunched_Launching_MarksRunningWithHistory()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        DateTimeOffset now = host.Time.GetUtcNow();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, now);
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordRunLaunched, Unit>(new RecordRunLaunched(run.Id, 55, now.AddSeconds(-1)));

        run.State.ShouldBe(RunState.Running);
        run.Pid.ShouldBe(55);
        run.ProcessStartTime.ShouldBe(now.AddSeconds(-1));
        run.StartedAt.ShouldBe(now);
        host.Store.History.ShouldContain(h => h.Kind == HistoryEntryKind.RunStarted && h.RunId == run.Id);
    }

    [Fact]
    public async Task RecordRunLaunchFailed_UnknownOrCompletedRun_LoggedAndUnchanged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(RunState.Completed).Build();
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordRunLaunchFailed, Unit>(new RecordRunLaunchFailed(RunId.New(), "boom"));
        await host.Run<RecordRunLaunchFailed, Unit>(new RecordRunLaunchFailed(run.Id, "boom"));

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.Error.ShouldBeNull();
        host.Log.Entries.Count(e => e.Category == "RecordRunLaunchFailedHandler").ShouldBe(2);
    }

    [Fact]
    public async Task RecordLaunchAborted_UnknownOrCompletedRun_LoggedAndUnchanged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(RunState.Completed).Build();
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordLaunchAborted, Unit>(new RecordLaunchAborted(RunId.New()));
        await host.Run<RecordLaunchAborted, Unit>(new RecordLaunchAborted(run.Id));

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.Log.Entries.Count(e => e.Category == "RecordLaunchAbortedHandler").ShouldBe(2);
    }

    [Fact]
    public async Task RecordRunExited_UnknownOrCompletedRun_LoggedAndUnchanged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(RunState.Completed).Build();
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(RunId.New(), 1, false));
        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.ExitCode.ShouldBe(0);
        host.Log.Entries.Count(e => e.Category == "RecordRunExitedHandler").ShouldBe(2);
    }

    [Fact]
    public async Task RecordRunExited_Launching_RuleViolationAndNothingChanged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(RunState.Launching).Build();
        host.Store.Runs[run.Id] = run;

        await Should.ThrowAsync<DomainException>(
            () => host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 0, false)));

        run.State.ShouldBe(RunState.Launching);
    }

    [Fact]
    public async Task RecordRunExited_ProcessMissing_RunStillCompletedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(RunState.Running).Build();
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 0, false));

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.Log.Entries.ShouldContain(e => e.Level == MsLogLevel.Warning);
    }

    [Fact]
    public async Task RecordRunLaunchFailed_ProcessMissing_RunStillCompletedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Run run = new RunBuilder().InState(RunState.Launching).Build();
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordRunLaunchFailed, Unit>(new RecordRunLaunchFailed(run.Id, "boom"));

        run.Outcome.ShouldBe(RunOutcome.FailedToStart);
        host.Log.Entries.ShouldContain(e => e.Level == MsLogLevel.Warning);
    }

    [Fact]
    public async Task RecordRunExited_FailureLimitReached_BlocksUsingTheStoredLimit()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ChangeSettings(host, failureLimit: 1);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        run.Outcome.ShouldBe(RunOutcome.Failed);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.RetryPending.ShouldBeFalse();
        host.Store.History.ShouldContain(h => h.Kind == HistoryEntryKind.ProcessBlocked);
    }

    [Fact]
    public async Task RecordRunExited_Killed_KeepsTheFlagAndUsesOneInstant()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        run.RequestStop(StopReason.TimeLimit, host.Time.GetUtcNow());
        host.Time.Advance(TimeSpan.FromMinutes(1));

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, -1, true));

        run.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        run.EndedAt.ShouldBe(host.Time.GetUtcNow());
        process.UpdatedAt.ShouldBe(host.Time.GetUtcNow());
    }

    // Review Focus 5
    [Fact]
    public async Task RecordLaunchAborted_ShutdownStop_EndsInterruptedWithoutFailure()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        DateTimeOffset now = host.Time.GetUtcNow();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, now);
        run.RequestStop(StopReason.Shutdown, now);
        host.Store.Runs[run.Id] = run;

        await host.Run<RecordLaunchAborted, Unit>(new RecordLaunchAborted(run.Id));

        run.Outcome.ShouldBe(RunOutcome.Interrupted);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
    }

    [Fact]
    public async Task StartPendingRun_Eligible_ConsumesDueOccurrencesAndStartsRestartRun()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset start = host.Time.GetUtcNow();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start.AddHours(1))])));
        Process process = host.Store.Processes[saved.Id];
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Time.Advance(TimeSpan.FromHours(5));

        Run run = await host.StartRunAsync(process);

        process.NextScheduledAt!.Value.ShouldBeGreaterThan(host.Time.GetUtcNow());
        run.Trigger.ShouldBe(RunTrigger.Restart);
        run.ScheduledFor.ShouldBeNull();
        run.IsLate.ShouldBeFalse();
        run.CreatedAt.ShouldBe(host.Time.GetUtcNow());
        run.ProcessNameSnapshot.ShouldBe(process.Name);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task StartPendingRun_DisabledRunToCompletion_StillStarts()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.Disable(host.Time.GetUtcNow());
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);

        run.Trigger.ShouldBe(RunTrigger.Restart);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task StartPendingRun_Paused_SkippedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        await host.Run<StartPendingRun, Unit>(new StartPendingRun(process.Id));

        AssertSkipped(host);
    }

    [Fact]
    public async Task StartPendingRun_Blocked_SkippedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 1, host.Time.GetUtcNow());
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);

        await host.Run<StartPendingRun, Unit>(new StartPendingRun(process.Id));

        AssertSkipped(host);
    }

    [Fact]
    public async Task StartPendingRun_DisabledService_SkippedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.Disable(host.Time.GetUtcNow());

        await host.Run<StartPendingRun, Unit>(new StartPendingRun(process.Id));

        AssertSkipped(host);
    }

    [Fact]
    public async Task StartPendingRun_ProcessMissing_SkippedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        await host.Run<StartPendingRun, Unit>(new StartPendingRun(ProcessId.New()));

        AssertSkipped(host);
    }

    // BI-041
    [Fact]
    public async Task StartPendingRun_ActiveRun_SkippedAndLogged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        Run active = host.AddRunningRun(process);

        await host.Run<StartPendingRun, Unit>(new StartPendingRun(process.Id));

        host.Store.Runs.Keys.ShouldBe([active.Id]);
        host.Launcher.LaunchRequests.ShouldBeEmpty();
        host.LoggedAt(MsLogLevel.Information, "StartPendingRunHandler").ShouldBeTrue();
    }

    [Fact]
    public async Task RunStarter_SecretVariable_IsUnprotectedInRequest()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(
            environment:
            [
                new EnvironmentVariableInput("API_TOKEN", SecretValue, true),
                new EnvironmentVariableInput("MODE", "fast", false),
            ])));
        Process process = host.Store.Processes[saved.Id];
        process.Launch.Environment.Items[0].SecretValue!.Ciphertext.ShouldNotBe(SecretValue);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await host.EndOf(run.Id);

        LaunchRequest request = host.Launcher.LaunchRequests.Single();
        request.RunId.ShouldBe(run.Id);
        request.Environment.ShouldBe(
        [
            new KeyValuePair<string, string>("API_TOKEN", SecretValue),
            new KeyValuePair<string, string>("MODE", "fast"),
        ]);
        host.Log.Entries.ShouldNotContain(e => e.Message.Contains(SecretValue, StringComparison.Ordinal));
    }

    [Fact]
    public void LaunchRequest_ToString_DoesNotExposeEnvironmentValues()
    {
        RunId runId = RunId.New();
        var request = new LaunchRequest(
            runId, Exe, string.Empty, @"C:\Tools", [new KeyValuePair<string, string>("API_TOKEN", SecretValue)]);
        var ticket = new RunStartTicket(runId, ProcessId.New(), request, null, TimeSpan.FromSeconds(1));

        request.ToString().ShouldNotContain(SecretValue);
        ticket.ToString().ShouldNotContain(SecretValue);
    }

    [Fact]
    public async Task RunStarter_NoWorkingDirectory_UsesExecutableFolder()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await host.EndOf(run.Id);

        LaunchRequest request = host.Launcher.LaunchRequests.Single();
        request.Executable.ShouldBe(Exe);
        request.WorkingDirectory.ShouldBe(process.Launch.EffectiveWorkingDirectory);
        request.WorkingDirectory.ShouldBe(Path.GetDirectoryName(Exe));
    }

    [Fact]
    public async Task RunStarter_ConfiguredLaunchOptions_ArePassedInRequest()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic() with { Arguments = "--all", WorkingDirectory = @"C:\Work" }));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(host.Store.Processes[saved.Id]);
        child.Exit(0);
        await host.EndOf(run.Id);

        LaunchRequest request = host.Launcher.LaunchRequests.Single();
        request.Arguments.ShouldBe("--all");
        request.WorkingDirectory.ShouldBe(@"C:\Work");
    }

    [Fact]
    public async Task RunStarter_ServiceKind_HasNoTimeLimit()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ChangeSettings(host, defaultTimeLimit: TimeSpan.FromHours(1));
        Process process = host.AddProcess(ProcessKind.Service);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        child.Exit(1);
        await host.EndOf(run.Id);

        host.Time.TimersCreated.ShouldBe(0);
        run.Outcome.ShouldBe(RunOutcome.Crashed);
    }

    [Fact]
    public async Task RunStarter_ProcessTimeLimit_WinsOverDefault()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ChangeSettings(host, defaultTimeLimit: TimeSpan.FromHours(8));
        Process process = host.AddProcess(timeLimit: TimeSpan.FromHours(1));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromHours(1));
        await host.Time.WaitForTimersAsync(2).Within();

        child.SignalCancelCount.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
    }

    [Fact]
    public async Task RunStarter_NoProcessTimeLimit_UsesDefault()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ChangeSettings(host, defaultTimeLimit: TimeSpan.FromHours(2));
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromHours(2));
        await host.Time.WaitForTimersAsync(2).Within();

        child.SignalCancelCount.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task RunStarter_ProcessGracePeriod_WinsOverDefault()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: TimeSpan.FromHours(1), gracePeriod: TimeSpan.FromSeconds(5));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromHours(1));
        await host.AdvanceAfterTimerAsync(2, TimeSpan.FromSeconds(5));
        await host.EndOf(run.Id);

        child.Killed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunStarter_NoProcessGracePeriod_UsesDefault()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: TimeSpan.FromHours(1));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromHours(1));
        await host.AdvanceAfterTimerAsync(2, InstallationSettings.Default().DefaultGracePeriod);
        await host.EndOf(run.Id);

        child.Killed.ShouldBeTrue();
    }

    private static void AssertSkipped(ApplicationTestHost host)
    {
        host.Store.Runs.ShouldBeEmpty();
        host.Launcher.LaunchRequests.ShouldBeEmpty();
        host.LoggedAt(MsLogLevel.Information, "StartPendingRunHandler").ShouldBeTrue();
    }

    private static void ChangeSettings(ApplicationTestHost host, int failureLimit = 3, TimeSpan? defaultTimeLimit = null)
    {
        InstallationSettings settings = host.Store.Settings;
        settings.Change(
            settings.CycleInterval,
            failureLimit,
            defaultTimeLimit,
            settings.DefaultGracePeriod,
            settings.LogRetentionDays,
            settings.HistoryRetentionDays,
            settings.LogLevel,
            settings.CheckForUpdates);
    }
}
