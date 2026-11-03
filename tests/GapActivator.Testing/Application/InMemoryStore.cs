using GapActivator.Application.Events;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Testing;

/// <summary>Shared state behind the in-memory repositories. Singleton in <see cref="ApplicationTestHost"/>.</summary>
public sealed class InMemoryStore
{
    private readonly HashSet<ProcessId> _failOnLoad = [];
    private int _failingCommits;
    private Exception? _commitError;

    /// <summary>
    /// Guards <see cref="Processes"/>, <see cref="Runs"/>, <see cref="History"/> and the purge records. The in-memory
    /// repositories take it for every read and write. Tests may read the collections after awaiting, but must not
    /// mutate them while commands are running.
    /// </summary>
    public Lock Sync { get; } = new();

    public Dictionary<ProcessId, Process> Processes { get; } = [];

    public Dictionary<RunId, Run> Runs { get; } = [];

    public List<HistoryEntry> History { get; } = [];

    public InstallationSettings Settings { get; set; } = InstallationSettings.Default();

    /// <summary>Cutoff passed to each history purge, in call order.</summary>
    public List<DateTimeOffset> HistoryPurgeCutoffs { get; } = [];

    /// <summary>Days passed to each log retention purge, in call order.</summary>
    public List<int> LogRetentionDays { get; } = [];

    /// <summary>
    /// The next <paramref name="count"/> commits of any scope throw <paramref name="error"/>. This replaces the
    /// remaining count rather than adding to it. A failed commit discards staged adds, removes and history entries,
    /// but in-place mutations of already loaded aggregates stay visible to later scopes (the fake does not clone).
    /// Do not assert "state unchanged after a failed commit" through this fake; Plan 3 integration tests cover rollback.
    /// </summary>
    public void FailNextCommits(int count, Exception error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(error);
        lock (Sync)
        {
            _failingCommits = count;
            _commitError = error;
        }
    }

    /// <summary>Loading this process by id throws, which simulates a fault in one process.</summary>
    public void FailOnLoad(ProcessId id)
    {
        lock (Sync)
        {
            _failOnLoad.Add(id);
        }
    }

    internal Exception? TakeCommitFailure()
    {
        lock (Sync)
        {
            if (_failingCommits == 0)
            {
                return null;
            }

            _failingCommits--;
            return _commitError;
        }
    }

    internal bool ShouldFailOnLoad(ProcessId id)
    {
        lock (Sync)
        {
            return _failOnLoad.Contains(id);
        }
    }
}
