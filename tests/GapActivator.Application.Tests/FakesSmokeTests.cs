using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Tests;

public class FakesSmokeTests
{
    [Fact]
    public async Task Commit_StagedAdd_IsAppliedOnlyOnCommit()
    {
        await using var host = new ApplicationTestHost();
        Process process = new ProcessBuilder().Build();

        using IServiceScope scope = host.CreateScope();
        scope.ServiceProvider.GetRequiredService<IProcessRepository>().Add(process);

        host.Store.Processes.ShouldBeEmpty();

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);

        host.Store.Processes.ShouldContainKey(process.Id);
    }

    [Fact]
    public async Task Commit_AfterFailNextCommits_ThrowsThenRecovers()
    {
        await using var host = new ApplicationTestHost();
        host.Store.FailNextCommits(1, new InvalidOperationException("boom"));

        using IServiceScope scope = host.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        scope.ServiceProvider.GetRequiredService<IProcessRepository>().Add(new ProcessBuilder().Build());

        await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        host.Store.Processes.ShouldBeEmpty();

        scope.ServiceProvider.GetRequiredService<IProcessRepository>().Add(new ProcessBuilder().Build());
        await unitOfWork.CommitAsync(CancellationToken.None);
        host.Store.Processes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DequeueDomainEvents_TrackedAggregates_ReturnsEventsOnceThenClears()
    {
        await using var host = new ApplicationTestHost();
        Run run = new RunBuilder().InState(RunState.Running).Build();
        run.RequestStop(StopReason.UserCancel, ApplicationTestHost.StartTime);
        run.DomainEvents.ShouldNotBeEmpty();

        using IServiceScope scope = host.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRunRepository>().Add(run);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        unitOfWork.DequeueDomainEvents().ShouldNotBeEmpty();
        unitOfWork.DequeueDomainEvents().ShouldBeEmpty();
        run.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetActiveForProcessAsync_CompletedRun_IsNotReturned()
    {
        await using var host = new ApplicationTestHost();
        Run active = new RunBuilder().InState(RunState.Running).Build();
        Run completed = new RunBuilder().InState(RunState.Completed).Build();
        host.Store.Runs[active.Id] = active;
        host.Store.Runs[completed.Id] = completed;

        using IServiceScope scope = host.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunRepository>();

        (await runs.ListActiveAsync(CancellationToken.None)).ShouldBe([active]);
    }

    [Fact]
    public async Task Exit_AndKill_CompleteExitedWithExpectedCodes()
    {
        await using var host = new ApplicationTestHost();
        FakeRunningProcess exiting = host.Launcher.QueueSucceed(@"C:\Tools\sample.exe", pid: 42);
        FakeRunningProcess killed = host.Launcher.QueueSucceed(@"C:\Tools\sample.exe");

        IRunningProcess first = await host.Launcher.LaunchAsync(Request(), CancellationToken.None);
        IRunningProcess second = await host.Launcher.LaunchAsync(Request(), CancellationToken.None);

        first.Pid.ShouldBe(42);
        exiting.Exit(1);
        (await first.Exited).ShouldBe(1);

        second.SignalCancel();
        await second.KillTreeAsync();
        (await second.Exited).ShouldBe(-1);
        killed.SignalCancelCount.ShouldBe(1);
        killed.Killed.ShouldBeTrue();
    }

    [Fact]
    public async Task LaunchAsync_QueuedFailure_ThrowsLaunchFailed()
    {
        await using var host = new ApplicationTestHost();
        host.Launcher.QueueFail(@"C:\Tools\sample.exe");

        await Should.ThrowAsync<LaunchFailedException>(
            () => host.Launcher.LaunchAsync(Request(), CancellationToken.None));
        host.Launcher.LaunchRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LaunchAsync_QueuedBlock_EndsWhenCancelled()
    {
        await using var host = new ApplicationTestHost();
        host.Launcher.QueueBlockUntilCancelled(@"C:\Tools\sample.exe");
        using var cts = new CancellationTokenSource();

        Task<IRunningProcess> launch = host.Launcher.LaunchAsync(Request(), cts.Token);
        launch.IsCompleted.ShouldBeFalse();

        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => launch);
    }

    [Fact]
    public void Protect_PlainText_AddsEncPrefix()
    {
        var protector = new FakeSecretProtector();

        protector.Protect("x").Ciphertext.ShouldBe("enc:x");
        protector.Unprotect(protector.Protect("x")).ShouldBe("x");
    }

    private static LaunchRequest Request() =>
        new(RunId.New(), @"C:\Tools\sample.exe", string.Empty, @"C:\Tools", []);
}
