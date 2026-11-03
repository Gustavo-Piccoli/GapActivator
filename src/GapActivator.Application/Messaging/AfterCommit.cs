namespace GapActivator.Application.Messaging;

/// <summary>
/// Scoped: work that must follow a successful commit, for example launching a process. The dispatcher runs the
/// actions in order after it has released the write gate. If the handler or the commit fails they are dropped.
/// </summary>
public sealed class AfterCommit
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    internal IReadOnlyList<Func<CancellationToken, Task>> Drain()
    {
        Func<CancellationToken, Task>[] actions = [.. _actions];
        _actions.Clear();
        return actions;
    }
}
