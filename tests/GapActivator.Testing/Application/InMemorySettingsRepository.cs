using GapActivator.Domain.Settings;

namespace GapActivator.Testing;

public sealed class InMemorySettingsRepository(InMemoryStore store, InMemoryUnitOfWork unitOfWork) : ISettingsRepository
{
    public Task<InstallationSettings> GetAsync(CancellationToken ct)
    {
        unitOfWork.Track(store.Settings);
        return Task.FromResult(store.Settings);
    }
}
