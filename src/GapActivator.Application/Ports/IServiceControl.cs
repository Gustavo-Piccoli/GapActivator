namespace GapActivator.Application.Ports;

public interface IServiceControl
{
    /// <summary>Schedules the restart and returns at once; the stop begins after the current command has published its events.</summary>
    Task RestartAsync(CancellationToken ct);
}
