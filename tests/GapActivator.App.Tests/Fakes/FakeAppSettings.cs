using GapActivator.App.Localization;
using GapActivator.App.Settings;

namespace GapActivator.App.Tests.Fakes;

/// <summary>In-memory app settings: applies an update, records it and raises <c>Changed</c>, with no file.</summary>
internal sealed class FakeAppSettings : IAppSettingsService
{
    public FakeAppSettings(AppSettings? initial = null) =>
        Current = initial ?? new AppSettings(AppTheme.System, AppLanguage.English, true, true, null, null);

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current { get; private set; }

    public int Updates { get; private set; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        Updates++;
        Changed?.Invoke(this, Current);
    }
}
