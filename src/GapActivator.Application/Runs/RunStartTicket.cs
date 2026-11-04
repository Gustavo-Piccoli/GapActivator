using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Runs;

/// <summary>What the supervisor needs to launch a committed run. The request holds unprotected secrets: never log it.</summary>
public sealed record RunStartTicket(
    RunId RunId,
    ProcessId ProcessId,
    LaunchRequest Request,
    TimeSpan? TimeLimit,
    TimeSpan GracePeriod);
