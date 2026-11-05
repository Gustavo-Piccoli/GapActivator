using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

/// <summary>
/// What failures the user has already looked at, per this app session: opening the history or a process marks the failures
/// before that moment as seen. Nothing is stored; at startup every failure is unseen.
/// </summary>
public sealed class FailureAcknowledgements(TimeProvider time)
{
    private readonly Dictionary<Guid, DateTimeOffset> _processSeen = [];
    private DateTimeOffset? _historySeen;

    public event EventHandler? Changed;

    public void MarkHistorySeen()
    {
        _historySeen = time.GetUtcNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkProcessSeen(Guid processId)
    {
        _processSeen[processId] = time.GetUtcNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True when the failure ended after both the last history visit and the last visit to its process; never visited counts as unseen.</summary>
    public bool IsUnseen(RunRowDto failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        DateTimeOffset? seen = _historySeen;
        if (_processSeen.TryGetValue(failure.ProcessId, out DateTimeOffset process) && (seen is null || process > seen))
        {
            seen = process;
        }

        return seen is null || (failure.EndedAt ?? failure.CreatedAt) > seen;
    }
}
