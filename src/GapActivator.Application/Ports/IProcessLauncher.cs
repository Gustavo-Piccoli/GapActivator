using GapActivator.Domain.Runs;

namespace GapActivator.Application.Ports;

/// <param name="RunId">The run being launched.</param>
/// <param name="Executable">Full path of the program.</param>
/// <param name="Arguments">Command-line arguments.</param>
/// <param name="WorkingDirectory">Directory the process starts in.</param>
/// <param name="Environment">Variables to set; secrets are already unprotected.</param>
public sealed record LaunchRequest(
    RunId RunId,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyList<KeyValuePair<string, string>> Environment);

public interface IRunningProcess : IAsyncDisposable
{
    int Pid { get; }

    DateTimeOffset StartTime { get; }

    /// <summary>The exit code; completes once.</summary>
    Task<int> Exited { get; }

    /// <summary>Idempotent.</summary>
    void SignalCancel();

    Task KillTreeAsync();
}

public sealed class LaunchFailedException(string message, Exception? inner = null) : Exception(message, inner);

public interface IProcessLauncher
{
    /// <exception cref="LaunchFailedException">The process could not be started.</exception>
    /// <exception cref="OperationCanceledException">The token fired before the OS process existed.</exception>
    Task<IRunningProcess> LaunchAsync(LaunchRequest request, CancellationToken ct);

    /// <summary>Returns null when no live process matches both the pid and the start time.</summary>
    IRunningProcess? TryAttach(RunId runId, int pid, DateTimeOffset processStartTime);
}
