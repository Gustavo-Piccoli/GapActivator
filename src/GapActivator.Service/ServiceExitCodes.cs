namespace GapActivator.Service;

/// <summary>The exit codes the service reports to the Service Control Manager.</summary>
public static class ServiceExitCodes
{
    public const int Normal = 0;

    /// <summary>A fatal startup or run failure; the service's recovery actions start it again.</summary>
    public const int Fatal = 1;

    /// <summary>A restart was requested; the service's recovery actions start it again.</summary>
    public const int Restart = 3;
}
