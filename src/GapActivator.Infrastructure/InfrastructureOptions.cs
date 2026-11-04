using GapActivator.Infrastructure.Email;

namespace GapActivator.Infrastructure;

/// <summary>Settings of the infrastructure adapters that are not stored in the database.</summary>
public sealed record InfrastructureOptions(GapActivatorPaths Paths)
{
    /// <summary>Cap on one run's output file (50 MiB); the pipe keeps draining past it.</summary>
    public long RunOutputMaxBytes { get; init; } = 52_428_800;

    /// <summary>How long a tree kill waits for the child to exit before giving up.</summary>
    public TimeSpan KillWaitTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public EmailSenderOptions Email { get; init; } = new();
}
