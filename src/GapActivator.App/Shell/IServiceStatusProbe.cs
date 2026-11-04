namespace GapActivator.App.Shell;

public enum ServiceInstallState
{
    NotInstalled,
    Stopped,
    StartPending,
    Running,
    Unknown,
}

/// <summary>Asks the Service Control Manager whether the GapActivator service is installed and running.</summary>
public interface IServiceStatusProbe
{
    ServiceInstallState Query();
}
