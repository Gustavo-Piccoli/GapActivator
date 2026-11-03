using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Application.Tests.Messaging;

public class CommandDispatcherTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Send_ViewerCaller_IsRejectedUnauthorized()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<ProbeCommand, int>(new ProbeCommand(), Caller.Viewer("viewer")));

        error.Code.ShouldBe(RejectionCodes.Unauthorized);
        state.Handled.ShouldBe(0);
        host.Store.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task Send_InternalCommandFromOperator_IsRejectedUnauthorized()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<ProbeInternalCommand, int>(new ProbeInternalCommand(), Caller.Operator("operator")));

        error.Code.ShouldBe(RejectionCodes.Unauthorized);
    }

    [Fact]
    public async Task Send_InternalCommandFromSystem_Runs()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        int result = await host.Run<ProbeInternalCommand, int>(new ProbeInternalCommand(), Caller.System);

        result.ShouldBe(7);
    }

    [Fact]
    public async Task Send_OperatorCaller_RunsPublicCommand()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        int result = await host.Run<ProbeCommand, int>(new ProbeCommand(), Caller.Operator("operator"));

        result.ShouldBe(42);
    }

    [Fact]
    public async Task Send_Success_CommitsThenRunsActionsThenPublishes()
    {
        var state = new ProbeState();
        state.Actions.Add(_ => state.NoteAsync("action"));
        await using ApplicationTestHost host = ProbeHost.Create(state);

        await host.Run<ProbeCommand, int>(new ProbeCommand());

        state.Order.ShouldBe(["commit", "action", "event"]);
    }

    [Fact]
    public async Task Send_Success_RunsActionsAndNotificationHandlersAfterTheGateIsReleased()
    {
        var state = new ProbeState();
        bool heldDuringAction = true;
        WriteGate? gate = null;
        state.Actions.Add(_ =>
        {
            heldDuringAction = gate!.IsHeld;
            return Task.CompletedTask;
        });

        await using ApplicationTestHost host = ProbeHost.Create(state);
        gate = host.Services.GetRequiredService<WriteGate>();

        await host.Run<ProbeCommand, int>(new ProbeCommand());

        heldDuringAction.ShouldBeFalse();
        state.GateHeldDuringEvent.ShouldBe(false);
        gate.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Send_ActionRuns_WithTheCancellationTokenOfTheDispatch()
    {
        var state = new ProbeState();
        CancellationToken seen = default;
        state.Actions.Add(ct =>
        {
            seen = ct;
            return Task.CompletedTask;
        });
        await using ApplicationTestHost host = ProbeHost.Create(state);
        using var cts = new CancellationTokenSource();

        await host.Send<ProbeCommand, int>(new ProbeCommand(), ct: cts.Token);

        seen.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Send_HandlerThrows_NothingCommittedOrPublished()
    {
        var state = new ProbeState { OnHandle = _ => throw new InvalidOperationException("handler failed") };
        state.Actions.Add(_ => state.NoteAsync("action"));
        await using ApplicationTestHost host = ProbeHost.Create(state);

        await Should.ThrowAsync<InvalidOperationException>(() => host.Run<ProbeCommand, int>(new ProbeCommand()));

        state.Order.ShouldBeEmpty();
        host.Store.History.ShouldBeEmpty();
        host.Services.GetRequiredService<WriteGate>().IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Send_CommitThrows_ActionsDroppedAndGateReleased()
    {
        var state = new ProbeState();
        state.Actions.Add(_ => state.NoteAsync("action"));
        await using ApplicationTestHost host = ProbeHost.Create(state);
        host.Store.FailNextCommits(1, new InvalidOperationException("commit failed"));

        await Should.ThrowAsync<InvalidOperationException>(() => host.Run<ProbeCommand, int>(new ProbeCommand()));

        state.Order.ShouldBeEmpty();
        host.Store.History.ShouldBeEmpty();

        int second = await host.Run<ProbeCommand, int>(new ProbeCommand()).WaitAsync(Safety, TestContext.Current.CancellationToken);

        second.ShouldBe(42);
        state.Order.ShouldBe(["commit", "action", "event"]);
        host.Store.History.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Send_DomainEvents_AreWrittenToHistoryInTheSameCommit()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        await host.Run<ProbeCommand, int>(new ProbeCommand());

        state.HistoryBeforeCommit.ShouldBe(0);
        state.HistoryAfterCommit.ShouldBe(1);
        HistoryEntry entry = host.Store.History.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(HistoryEntryKind.InstallationPaused);
        entry.OccurredAt.ShouldBe(host.Time.GetUtcNow());
        entry.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Send_ActionThrows_LaterActionsAndEventsStillRun()
    {
        var state = new ProbeState();
        state.Actions.Add(_ => throw new InvalidOperationException("action failed"));
        state.Actions.Add(_ => state.NoteAsync("action2"));
        await using ApplicationTestHost host = ProbeHost.Create(state);

        int result = await host.Run<ProbeCommand, int>(new ProbeCommand());

        result.ShouldBe(42);
        state.Order.ShouldBe(["commit", "action2", "event"]);
        host.Log.Entries.Any(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException).ShouldBeTrue();
    }

    [Fact]
    public async Task Send_ActionCancelledAfterCommit_RemainingActionsAndPublishStillRunAndResultReturned()
    {
        var state = new ProbeState();
        using var cts = new CancellationTokenSource();
        state.Actions.Add(async ct =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        });
        state.Actions.Add(_ => state.NoteAsync("action2"));
        await using ApplicationTestHost host = ProbeHost.Create(state);

        int result = await host.Send<ProbeCommand, int>(new ProbeCommand(), ct: cts.Token);

        result.ShouldBe(42);
        state.Order.ShouldBe(["commit", "action2", "event"]);
        host.Log.Entries.Any(e => e.Level == LogLevel.Information && e.Exception is OperationCanceledException)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Send_PublishThrowsAfterCommit_ResultStillReturned()
    {
        var state = new ProbeState();
        state.Actions.Add(_ => state.NoteAsync("action"));
        await using ApplicationTestHost host = ProbeHost.Create(
            state,
            services => services.AddScoped<INotificationHandler>(
                _ => throw new InvalidOperationException("cannot resolve handler")));

        int result = await host.Run<ProbeCommand, int>(new ProbeCommand());

        result.ShouldBe(42);
        state.Order.ShouldBe(["commit", "action"]);
        host.Store.History.Count.ShouldBe(1);
        host.Log.Entries.Any(e => e.Level == LogLevel.Error && e.Exception?.Message == "cannot resolve handler")
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Send_CancelledWhileWaitingForGate_ThrowsAndGateStillUsable()
    {
        var state = new ProbeState();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        state.OnHandle = async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
        };
        await using ApplicationTestHost host = ProbeHost.Create(state);
        using var cts = new CancellationTokenSource();

        Task<int> first = host.Run<ProbeCommand, int>(new ProbeCommand());
        await firstEntered.Task.WaitAsync(Safety, TestContext.Current.CancellationToken);
        Task<int> second = host.Send<ProbeCommand, int>(new ProbeCommand(), ct: cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => second);

        releaseFirst.SetResult();
        await first.WaitAsync(Safety, TestContext.Current.CancellationToken);
        int third = await host.Run<ProbeCommand, int>(new ProbeCommand())
            .WaitAsync(Safety, TestContext.Current.CancellationToken);

        third.ShouldBe(42);
        state.Handled.ShouldBe(2);
    }

    [Fact]
    public async Task Send_Unauthorized_RejectedWhileGateIsHeld()
    {
        var state = new ProbeState();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.OnHandle = async _ =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
        };
        await using ApplicationTestHost host = ProbeHost.Create(state);

        Task<int> first = host.Run<ProbeCommand, int>(new ProbeCommand());
        await firstEntered.Task.WaitAsync(Safety, TestContext.Current.CancellationToken);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<ProbeCommand, int>(new ProbeCommand(), Caller.Viewer("viewer"))
                .WaitAsync(Safety, TestContext.Current.CancellationToken));

        error.Code.ShouldBe(RejectionCodes.Unauthorized);
        state.Handled.ShouldBe(1);

        releaseFirst.SetResult();
        await first.WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Send_TwoConcurrentCommands_NeverOverlap()
    {
        var state = new ProbeState();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        state.OnHandle = async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
        };
        await using ApplicationTestHost host = ProbeHost.Create(state);

        Task<int> first = host.Run<ProbeCommand, int>(new ProbeCommand());
        await firstEntered.Task.WaitAsync(Safety, TestContext.Current.CancellationToken);
        Task<int> second = host.Run<ProbeCommand, int>(new ProbeCommand());

        second.IsCompleted.ShouldBeFalse();
        Volatile.Read(ref calls).ShouldBe(1);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Safety, TestContext.Current.CancellationToken);

        state.MaxInFlight.ShouldBe(1);
        Volatile.Read(ref calls).ShouldBe(2);
    }

    [Fact]
    public async Task Send_UngatedCommand_RunsWhileGateIsHeld()
    {
        var state = new ProbeState();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.OnHandle = async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
        };
        await using ApplicationTestHost host = ProbeHost.Create(state);

        Task<int> gated = host.Run<ProbeCommand, int>(new ProbeCommand());
        await firstEntered.Task.WaitAsync(Safety, TestContext.Current.CancellationToken);
        state.Actions.Add(_ => state.NoteAsync("ungated-action"));

        int result = await host.Run<ProbeUngatedCommand, int>(new ProbeUngatedCommand()).WaitAsync(Safety, TestContext.Current.CancellationToken);

        result.ShouldBe(1);
        gated.IsCompleted.ShouldBeFalse();
        state.Order.ShouldBe(["ungated-action", "event"]);
        host.Store.History.ShouldBeEmpty();

        releaseFirst.SetResult();
        await gated.WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Send_UngatedCommand_PublishesBufferedEventsWithoutHistory()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        await host.Run<ProbeUngatedCommand, int>(new ProbeUngatedCommand());

        state.Published.ShouldHaveSingleItem().ShouldBeOfType<CycleFailed>().Error.ShouldBe("probe failure");
        host.Store.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_Handler_SeesTheCaller()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        string name = await host.Ask<ProbeQuery, string>(new ProbeQuery(), Caller.Viewer("viewer-1"));

        name.ShouldBe("viewer-1");
    }

    [Fact]
    public async Task Query_WithoutCaller_UsesSystem()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        string name = await host.Ask<ProbeQuery, string>(new ProbeQuery());

        name.ShouldBe(Caller.System.Name);
    }
}
