namespace GapActivator.Application.Ports;

public interface IServiceControl
{
    Task RestartAsync(CancellationToken ct);
}
