namespace GapActivator.Application.Ports;

/// <summary>Purges log files and run output files.</summary>
public interface ILogRetention
{
    Task PurgeOlderThanAsync(int days, CancellationToken ct);
}
