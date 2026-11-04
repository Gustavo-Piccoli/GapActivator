using GapActivator.Application.Messaging;
using GapActivator.Application.Processes;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Application.Tests.Processes;

public class LifecycleCommandTests
{
    private const string Exe = RunTestHost.Executable;

    [Fact]
    public async Task Enable_Service_StartsAtOnce()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.Disable(host.Time.GetUtcNow());
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.Run<EnableProcess, Unit>(new EnableProcess(process.Id));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.ServiceKeepAlive);
        run.CreatedAt.ShouldBe(host.Time.GetUtcNow());
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Enable_ServiceWhilePaused_EnablesWithoutStarting()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.Disable(host.Time.GetUtcNow());
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        await host.Run<EnableProcess, Unit>(new EnableProcess(process.Id));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        host.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enable_RunToCompletion_EnablesWithoutStarting()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.Disable(host.Time.GetUtcNow());

        await host.Run<EnableProcess, Unit>(new EnableProcess(process.Id));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        host.Store.Runs.ShouldBeEmpty();
    }

    // Q-11
    [Fact]
    public async Task Enable_DiscardsOccurrencesMissedWhileDisabled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = new ProcessBuilder()
            .WithExecutable(Exe)
            .WithSchedule(OneTimeSchedule.Create(now - TimeSpan.FromHours(1)))
            .Build();
        host.Store.Processes[process.Id] = process;
        process.Disable(now);

        await host.Run<EnableProcess, Unit>(new EnableProcess(process.Id));

        process.Schedules.OfType<OneTimeSchedule>().Single().FulfilledAt.ShouldBe(now);
        host.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enable_CounterAtLimit_BlocksInsteadOfStarting()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddProcess(ProcessKind.Service);
        for (int i = 0; i < 3; i++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, 10, now);
        }

        process.Disable(now);

        await host.Run<EnableProcess, Unit>(new EnableProcess(process.Id));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        host.Store.Runs.ShouldBeEmpty();
        host.Launcher.LaunchRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enable_AlreadyEnabled_ThrowsDomainException()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();

        await Should.ThrowAsync<DomainException>(
            () => host.Run<EnableProcess, Unit>(new EnableProcess(process.Id)));
    }

    [Fact]
    public async Task Enable_UnknownProcess_IsRejectedNotFound()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<EnableProcess, Unit>(new EnableProcess(ProcessId.New())));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    [Fact]
    public async Task Disable_RunToCompletion_LetsCurrentRunFinish()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<DisableProcess, Unit>(new DisableProcess(process.Id, false));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        run.StopReason.ShouldBeNull();
        child.SignalCancelCount.ShouldBe(0);

        child.Exit(0);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    [Fact]
    public async Task Disable_WithCancel_EndsCancelled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<DisableProcess, Unit>(new DisableProcess(process.Id, true));
        await child.Signalled.Within();
        child.Exit(1);
        await host.EndOf(run.Id);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        run.StopReason.ShouldBe(StopReason.UserCancel);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
    }

    [Fact]
    public async Task Disable_Service_StopsRunAsStopped()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<DisableProcess, Unit>(new DisableProcess(process.Id, false));
        await child.Signalled.Within();
        child.Exit(0);
        await host.EndOf(run.Id);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        run.StopReason.ShouldBe(StopReason.UserCancel);
        run.Outcome.ShouldBe(RunOutcome.Stopped);
    }

    [Fact]
    public async Task Disable_Idle_JustDisables()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();

        await host.Run<DisableProcess, Unit>(new DisableProcess(process.Id, true));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
    }

    [Fact]
    public async Task Disable_AlreadyDisabled_ThrowsDomainException()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.Disable(host.Time.GetUtcNow());

        await Should.ThrowAsync<DomainException>(
            () => host.Run<DisableProcess, Unit>(new DisableProcess(process.Id, true)));
    }

    [Fact]
    public async Task Disable_UnknownProcess_IsRejectedNotFound()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<DisableProcess, Unit>(new DisableProcess(ProcessId.New(), false)));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    [Fact]
    public async Task Unblock_WithRunNow_StartsManualRun()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, true));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(0);
        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.Manual);
        run.CreatedAt.ShouldBe(host.Time.GetUtcNow());

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Unblock_WithoutRunNow_OnlyUnblocks()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);

        await host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, false));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(0);
        host.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unblock_WithRunNowWhilePaused_IsRejectedAndStaysBlocked()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, true)));

        error.Code.ShouldBe(RejectionCodes.InstallationPaused);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        host.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unblock_WithoutRunNowWhilePaused_Unblocks()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        await host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, false));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
    }

    [Fact]
    public async Task Unblock_WithRunNowAndActiveRun_IsRejectedAndStaysBlocked()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.ApplyRunOutcome(RunOutcome.Failed, 1, host.Time.GetUtcNow());
        host.AddRunningRun(process);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, true)));

        error.Code.ShouldBe(RejectionCodes.RunAlreadyActive);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        host.Store.Runs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unblock_DiscardedOneTime_IsFulfilledWithoutRun()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = new ProcessBuilder()
            .WithExecutable(Exe)
            .WithSchedule(OneTimeSchedule.Create(now - TimeSpan.FromHours(1)))
            .Build();
        host.Store.Processes[process.Id] = process;
        process.ApplyRunOutcome(RunOutcome.Failed, 1, now);

        await host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, false));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.Schedules.OfType<OneTimeSchedule>().Single().FulfilledAt.ShouldBe(now);
        host.Store.Runs.ShouldBeEmpty();
        host.Launcher.LaunchRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unblock_NotBlocked_ThrowsDomainException()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();

        await Should.ThrowAsync<DomainException>(
            () => host.Run<UnblockProcess, Unit>(new UnblockProcess(process.Id, false)));
    }

    [Fact]
    public async Task Unblock_UnknownProcess_IsRejectedNotFound()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<UnblockProcess, Unit>(new UnblockProcess(ProcessId.New(), false)));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    private static Process AddBlockedProcess(ApplicationTestHost host)
    {
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 1, host.Time.GetUtcNow());
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        return process;
    }
}
