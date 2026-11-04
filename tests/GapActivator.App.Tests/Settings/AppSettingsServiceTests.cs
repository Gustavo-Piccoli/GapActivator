using System.Globalization;
using GapActivator.App.Localization;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Settings;

public class AppSettingsServiceTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Update_SavesThenRaisesChanged()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        var store = new AppSettingsStore(path, new ListLogger<AppSettingsStore>(), English);
        var service = new AppSettingsService(store, AppSettings.Defaults(English));
        AppSettings? seenOnDisk = null;
        AppSettings? raised = null;
        service.Changed += (_, current) =>
        {
            raised = current;
            seenOnDisk = store.Load();
        };

        service.Update(s => s with { Theme = AppTheme.Dark, Language = AppLanguage.Portuguese });

        service.Current.Theme.ShouldBe(AppTheme.Dark);
        raised.ShouldBe(service.Current);
        seenOnDisk.ShouldBe(service.Current);
    }

    [Fact]
    public void Update_SaveFails_KeepsTheValueInMemory()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        Directory.CreateDirectory(path);
        var service = new AppSettingsService(new AppSettingsStore(path, new ListLogger<AppSettingsStore>(), English), AppSettings.Defaults(English));
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Update(s => s with { Notifications = false });

        service.Current.Notifications.ShouldBeFalse();
        raised.ShouldBe(1);
    }
}
