using GapActivator.Application.Messaging;
using GapActivator.Application.Queries;
using GapActivator.Application.Runs;
using GapActivator.Domain.Runs;
using GapActivator.Service.Tests.Support;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace GapActivator.Service.Tests;

/// <summary>
/// The real host, in-process, running the sample and test children on a seeded data root (done-when ruling 17). Each test
/// gets its own <see cref="E2EFixture"/>, because one of them stops the host.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan RunningWait = TimeSpan.FromSeconds(10);

    private E2EFixture? _fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private E2EFixture Fixture => _fixture ?? throw new InvalidOperationException("The fixture has not started.");

    public async ValueTask InitializeAsync() => _fixture = await E2EFixture.StartAsync(Ct);

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Scheduled_SampleRun_SucceedsWithCapturedOutput()
    {
        RunRow run = await Fixture.WaitForRunAsync(
            E2EFixture.ScheduledName, r => r.State == RunState.Completed, TimeSpan.FromSeconds(30), Ct);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.Trigger.ShouldBe(RunTrigger.Schedule);
        run.ExitCode.ShouldBe(0);
        RunDetails details = await Fixture.Query<GetRun, RunDetails>(new GetRun(run.Id), Ct);
        details.Output.Text.ShouldContain("Done.");
        details.Output.Text.ShouldContain($"Started by GapActivator (PID {Environment.ProcessId}).");
    }

    // BI-057
    [Fact]
    public async Task RunNow_ThenCancel_EndsCancelledThroughTheEventWithoutKill()
    {
        RunId id = await Fixture.Send<RunNow, RunId>(new RunNow(Fixture.ProcessId(E2EFixture.LongName)), Ct);
        await Fixture.WaitForRunAsync(E2EFixture.LongName, r => r.Id == id && r.State == RunState.Running, RunningWait, Ct);

        await Fixture.Send<CancelRun, Unit>(new CancelRun(id), Ct);

        RunRow run = await Fixture.WaitForRunAsync(
            E2EFixture.LongName, r => r.Id == id && r.State == RunState.Completed, TimeSpan.FromSeconds(10), Ct);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.Killed.ShouldBeFalse();
        RunDetails details = await Fixture.Query<GetRun, RunDetails>(new GetRun(id), Ct);
        details.Output.Text.ShouldContain("Cancellation requested; stopping cleanly.");
    }

    // BI-057
    [Fact]
    public async Task TimeLimit_ChildIgnoringCancel_EndsTimedOutKilledAfterGrace()
    {
        RunId id = await Fixture.Send<RunNow, RunId>(new RunNow(Fixture.ProcessId(E2EFixture.StubbornName)), Ct);

        RunRow run = await Fixture.WaitForRunAsync(
            E2EFixture.StubbornName, r => r.Id == id && r.State == RunState.Completed, TimeSpan.FromSeconds(15), Ct);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        run.Killed.ShouldBeTrue();
        (run.EndedAt.ShouldNotBeNull() - run.StartedAt.ShouldNotBeNull()).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3));
    }

    // BI-074, BI-075
    [Fact]
    public async Task StopHost_ActiveRun_EndsInterruptedAndNoChildLeft()
    {
        RunId id = await Fixture.Send<RunNow, RunId>(new RunNow(Fixture.ProcessId(E2EFixture.LongName)), Ct);
        await Fixture.WaitForRunAsync(E2EFixture.LongName, r => r.Id == id && r.State == RunState.Running, RunningWait, Ct);
        int pid = await Fixture.ActivePidAsync(E2EFixture.LongName, Ct);
        using DiagnosticsProcess child = DiagnosticsProcess.GetProcessById(pid);
        try
        {
            await Fixture.StopHostAsync();

            child.HasExited.ShouldBeTrue();
            await using E2ESeeding seeding = E2ESeeding.Open(Fixture.DataRoot);
            RunDetails details = await seeding.Query<GetRun, RunDetails>(new GetRun(id), Ct);
            details.Run.Outcome.ShouldBe(RunOutcome.Interrupted);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
    }
}
