using GapActivator.Application.Events;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Testing;

/// <summary>Shared state behind the in-memory repositories. Singleton in <see cref="ApplicationTestHost"/>.</summary>
public sealed class InMemoryStore
{
    private readonly Lock _gate = new();
    private readonly HashSet<ProcessId> _failOnLoad = [];
    private int _failingCommits;
    private Exception? _commitError;

    public Dictionary<ProcessId, Process> Processes { get; } = [];

    public Dictionary<RunId, Run> Runs { get; } = [];

    public List<HistoryEntry> History { get; } = [];

    public InstallationSettings Settings { get; set; } = InstallationSettings.Default();

    /// <summary>Cutoff passed to each history purge, in call order.</summary>
    public List<DateTimeOffset> HistoryPurgeCutoffs { get; } = [];

    /// <summary>Days passed to each log retention purge, in call order.</summary>
    public List<int> LogRetentionDays { get; } = [];

    /// <summary>The next <paramref name="count"/> commits of any scope throw <paramref name="error"/>.</summary>
    public void FailNextCommits(int count, Exception error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
        {
            _failingCommits = count;
            _commitError = error;
        }
    }

    /// <summary>Loading this process by id throws, which simulates a fault in one process.</summary>
    public void FailOnLoad(ProcessId id)
    {
        lock (_gate)
        {
            _failOnLoad.Add(id);
        }
    }

    internal Exception? TakeCommitFailure()
    {
        lock (_gate)
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
        lock (_gate)
        {
            return _failOnLoad.Contains(id);
        }
    }
}
