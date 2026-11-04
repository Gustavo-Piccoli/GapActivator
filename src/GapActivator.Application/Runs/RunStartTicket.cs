using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Runs;

/// <summary>
/// What the supervisor needs to launch a committed run. It holds no plaintext: secrets stay protected in
/// <paramref name="Environment"/> until the supervisor builds the launch request.
/// </summary>
public sealed record RunStartTicket(
    RunId RunId,
    ProcessId ProcessId,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    EnvironmentVariables Environment,
    TimeSpan? TimeLimit,
    TimeSpan GracePeriod);
