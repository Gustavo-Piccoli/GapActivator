using GapActivator.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Testing;

/// <summary>
/// Wraps the real dispatcher so a test can make sends of one command type fail, pause them, or await them. A scripted
/// failure throws before the real dispatcher runs, so nothing is mutated: <see cref="InMemoryStore.FailNextCommits"/>
/// cannot simulate a retry, because the fake keeps the in-place mutations of a failed commit.
/// Install it with <c>new ApplicationTestHost(ScriptedCommandDispatcher.Install)</c>.
/// </summary>
public sealed class ScriptedCommandDispatcher(ICommandDispatcher inner) : ICommandDispatcher
{
    private readonly Lock _sync = new();
    private readonly Dictionary<Type, Queue<Exception>> _failures = [];
    private readonly Dictionary<Type, CommandHold> _holdsBefore = [];
    private readonly Dictionary<Type, CommandHold> _holdsAfter = [];
    private readonly Dictionary<Type, int> _attempts = [];
    private readonly Dictionary<Type, int> _handled = [];
    private readonly List<(Type Command, int Count, TaskCompletionSource Signal)> _waiters = [];
    private readonly List<(Type Command, int Count, TaskCompletionSource Signal)> _attemptWaiters = [];

    /// <summary>Replaces <see cref="ICommandDispatcher"/> in a service collection with a scripted one.</summary>
    public static void Install(IServiceCollection services)
    {
        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton(sp => new ScriptedCommandDispatcher(sp.GetRequiredService<CommandDispatcher>()));
        services.AddSingleton<ICommandDispatcher>(sp => sp.GetRequiredService<ScriptedCommandDispatcher>());
    }

    /// <summary>The next <paramref name="count"/> sends of the command throw <paramref name="error"/> without reaching the real dispatcher.</summary>
    public void FailNext<TCommand>(int count, Exception? error = null)
    {
        lock (_sync)
        {
            if (!_failures.TryGetValue(typeof(TCommand), out Queue<Exception>? queue))
            {
                queue = new Queue<Exception>();
                _failures[typeof(TCommand)] = queue;
            }

            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(error ?? new InvalidOperationException("Simulated failure."));
            }
        }
    }

    /// <summary>Drops the failures still queued for the command, so its next send reaches the real dispatcher.</summary>
    public void StopFailing<TCommand>()
    {
        lock (_sync)
        {
            _failures.Remove(typeof(TCommand));
        }
    }

    /// <summary>Completes once at least <paramref name="count"/> sends of the command have started.</summary>
    public Task WaitForAttemptsAsync<TCommand>(int count)
    {
        lock (_sync)
        {
            if (_attempts.GetValueOrDefault(typeof(TCommand)) >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _attemptWaiters.Add((typeof(TCommand), count, signal));
            return signal.Task;
        }
    }

    /// <summary>The next send of the command waits, before it reaches the real dispatcher, until the hold is released.</summary>
    public CommandHold HoldBefore<TCommand>()
    {
        var hold = new CommandHold();
        lock (_sync)
        {
            _holdsBefore[typeof(TCommand)] = hold;
        }

        return hold;
    }

    /// <summary>The next send of the command runs to its end in the real dispatcher, then waits until the hold is released.</summary>
    public CommandHold HoldAfter<TCommand>()
    {
        var hold = new CommandHold();
        lock (_sync)
        {
            _holdsAfter[typeof(TCommand)] = hold;
        }

        return hold;
    }

    /// <summary>How many sends of the command started, including the ones that failed.</summary>
    public int Attempts<TCommand>()
    {
        lock (_sync)
        {
            return _attempts.GetValueOrDefault(typeof(TCommand));
        }
    }

    /// <summary>Completes once the real dispatcher has returned from at least <paramref name="count"/> sends of the command.</summary>
    public Task WaitForHandledAsync<TCommand>(int count = 1)
    {
        lock (_sync)
        {
            if (_handled.GetValueOrDefault(typeof(TCommand)) >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((typeof(TCommand), count, signal));
            return signal.Task;
        }
    }

    public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, Caller caller, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        Type key = typeof(TCommand);
        Exception? failure = null;
        CommandHold? before;
        lock (_sync)
        {
            int attempts = _attempts.GetValueOrDefault(key) + 1;
            _attempts[key] = attempts;
            foreach ((Type Command, int Count, TaskCompletionSource Signal) waiter in
                _attemptWaiters.Where(w => w.Command == key && w.Count <= attempts))
            {
                waiter.Signal.TrySetResult();
            }

            _attemptWaiters.RemoveAll(w => w.Command == key && w.Count <= attempts);
            _holdsBefore.Remove(key, out before);
            if (before is null && _failures.TryGetValue(key, out Queue<Exception>? queue))
            {
                queue.TryDequeue(out failure);
            }
        }

        if (before is not null)
        {
            await before.PassAsync();
        }

        if (failure is not null)
        {
            throw failure;
        }

        TResult result = await inner.SendAsync<TCommand, TResult>(command, caller, ct);

        CommandHold? after;
        lock (_sync)
        {
            int handled = _handled.GetValueOrDefault(key) + 1;
            _handled[key] = handled;
            foreach ((Type Command, int Count, TaskCompletionSource Signal) waiter in
                _waiters.Where(w => w.Command == key && w.Count <= handled))
            {
                waiter.Signal.TrySetResult();
            }

            _waiters.RemoveAll(w => w.Command == key && w.Count <= handled);
            _holdsAfter.Remove(key, out after);
        }

        if (after is not null)
        {
            await after.PassAsync();
        }

        return result;
    }
}

/// <summary>One paused send: <see cref="Reached"/> completes when the send arrives, <see cref="Release"/> lets it go on.</summary>
public sealed class CommandHold
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Reached => _reached.Task;

    public void Release() => _released.TrySetResult();

    internal Task PassAsync()
    {
        _reached.TrySetResult();
        return _released.Task;
    }
}
