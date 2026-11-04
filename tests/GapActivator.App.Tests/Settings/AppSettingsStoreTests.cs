using System.Globalization;
using System.Text;
using GapActivator.App.Localization;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Tests.Settings;

public class AppSettingsStoreTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static AppSettingsStore NewStore(string path, ListLogger<AppSettingsStore>? logger = null) =>
        new(path, logger ?? new ListLogger<AppSettingsStore>(), English);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var dir = new TempDirectory();

        var settings = NewStore(Path.Combine(dir.Path, "app-settings.json")).Load();

        settings.ShouldBe(AppSettings.Defaults(English));
        settings.Theme.ShouldBe(AppTheme.System);
        settings.Language.ShouldBe(AppLanguage.English);
        settings.Notifications.ShouldBeTrue();
        settings.CheckForUpdates.ShouldBeTrue();
        settings.LastUpdateCheck.ShouldBeNull();
        settings.Window.ShouldBeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        using var dir = new TempDirectory();
        var store = NewStore(Path.Combine(dir.Path, "app-settings.json"));
        var saved = new AppSettings(
            AppTheme.Dark,
            AppLanguage.Portuguese,
            false,
            false,
            new DateTimeOffset(2026, 11, 4, 10, 30, 0, TimeSpan.FromHours(-3)),
            new WindowBounds(10, 20, 900, 600, true));

        store.Save(saved).ShouldBeTrue();

        store.Load().ShouldBe(saved);
    }

    [Fact]
    public void Save_WritesTheDocumentWithStringEnums()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");

        NewStore(path).Save(AppSettings.Defaults(English)).ShouldBeTrue();

        File.ReadAllText(path).ShouldBe(
            """{"schemaVersion":1,"theme":"System","language":"English","notifications":true,"checkForUpdates":true,"lastUpdateCheck":null,"window":null}""");
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsKeepsTheCopyAndLogsAWarning()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        File.WriteAllText(path, "{ not json");
        File.WriteAllText(path + ".corrupt", "older copy");
        var logger = new ListLogger<AppSettingsStore>();

        var settings = NewStore(path, logger).Load();

        settings.ShouldBe(AppSettings.Defaults(English));
        File.Exists(path).ShouldBeFalse();
        File.ReadAllText(path + ".corrupt").ShouldBe("{ not json");
        logger.Levels.ShouldBe([LogLevel.Warning]);
    }

    [Fact]
    public void Load_UnknownTheme_ResetsOnlyTheTheme()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        File.WriteAllText(path, """{"schemaVersion":1,"theme":"Sepia","language":"Portuguese","notifications":false}""");
        var logger = new ListLogger<AppSettingsStore>();

        var settings = NewStore(path, logger).Load();

        settings.Theme.ShouldBe(AppTheme.System);
        settings.Language.ShouldBe(AppLanguage.Portuguese);
        settings.Notifications.ShouldBeFalse();
        logger.Levels.ShouldBe([LogLevel.Warning]);
    }

    [Fact]
    public void Load_CommentsAndTrailingCommas_Accepted()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        File.WriteAllText(path, "{\n // a comment\n \"theme\": \"Light\", /* inline */ \"notifications\": false,\n}");

        var settings = NewStore(path).Load();

        settings.Theme.ShouldBe(AppTheme.Light);
        settings.Notifications.ShouldBeFalse();
    }

    [Fact]
    public void Save_WritesUtf8WithoutBomAndLeavesNoTmp()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "sub", "app-settings.json");

        NewStore(path).Save(AppSettings.Defaults(English)).ShouldBeTrue();

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).ShouldNotBe(Encoding.UTF8.GetPreamble());
        bytes[0].ShouldBe((byte)'{');
        File.Exists(path + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Save_StaleTmpFromACrash_IsOverwritten()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        File.WriteAllText(path + ".tmp", new string('x', 2000));
        var store = NewStore(path);
        var saved = AppSettings.Defaults(English) with { Theme = AppTheme.Dark, Language = AppLanguage.Portuguese, Notifications = false };

        store.Save(saved).ShouldBeTrue();

        File.Exists(path + ".tmp").ShouldBeFalse();
        File.Exists(path + ".corrupt").ShouldBeFalse();
        File.ReadAllText(path).ShouldBe(
            """{"schemaVersion":1,"theme":"Dark","language":"Portuguese","notifications":false,"checkForUpdates":true,"lastUpdateCheck":null,"window":null}""");
        store.Load().ShouldBe(saved);
    }

    [Fact]
    public void Save_PathIsADirectory_ReturnsFalseAndLogsAWarning()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "app-settings.json");
        Directory.CreateDirectory(path);
        var logger = new ListLogger<AppSettingsStore>();

        NewStore(path, logger).Save(AppSettings.Defaults(English)).ShouldBeFalse();

        logger.Levels.ShouldBe([LogLevel.Warning]);
    }
}
