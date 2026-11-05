namespace GapActivator.App.Services;

public enum ServiceStartResult
{
    Started,
    Declined,
    Failed,
}

public sealed record ServiceStartOutcome(ServiceStartResult Result, int ErrorCode);

/// <summary>Starts the installed Windows service, which needs elevation.</summary>
public interface IServiceStarter
{
    Task<ServiceStartOutcome> StartAsync(CancellationToken ct);
}
