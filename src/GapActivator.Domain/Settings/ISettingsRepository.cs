namespace GapActivator.Domain.Settings;

public interface ISettingsRepository
{
    Task<InstallationSettings> GetAsync(CancellationToken ct);
}
