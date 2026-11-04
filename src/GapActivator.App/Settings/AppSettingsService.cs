namespace GapActivator.App.Settings;

public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    void Update(Func<AppSettings, AppSettings> change);
}

public sealed class AppSettingsService(AppSettingsStore store, AppSettings initial) : IAppSettingsService
{
    private readonly object _gate = new();
    private AppSettings _current = initial;

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings updated;
        lock (_gate)
        {
            updated = change(_current);
            _current = updated;

            // A failed save keeps the value in memory; the store logs it.
            _ = store.Save(updated);
        }

        Changed?.Invoke(this, updated);
    }
}
