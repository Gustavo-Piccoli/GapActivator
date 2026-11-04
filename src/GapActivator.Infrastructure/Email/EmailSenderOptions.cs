namespace GapActivator.Infrastructure.Email;

/// <summary>Time bounds of one SMTP send.</summary>
public sealed record EmailSenderOptions
{
    /// <summary>Bound on each connect, send and receive.</summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bound on the whole send.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(90);
}
