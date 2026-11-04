using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Logging;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Infrastructure.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class DatabaseInitializerTests : SqliteTestBase
{
    // BI-091
    [Fact]
    public async Task Initialize_EmptyFolder_CreatesFoldersDatabaseAndDefaultSettings()
    {
        Paths = new GapActivatorPaths(Path.Combine(Root.Path, "GapActivator"));
        var logLevel = new LogLevelSwitch();
        logLevel.Set(LogLevel.Error);

        await InitializeDatabaseAsync(logLevel);

        Directory.Exists(Paths.DataRoot).ShouldBeTrue();
        Directory.Exists(Paths.LogsDirectory).ShouldBeTrue();
        Directory.Exists(Paths.RunLogsDirectory).ShouldBeTrue();
        File.Exists(Paths.DatabaseFile).ShouldBeTrue();
        await using var db = NewContext();
        InstallationSettings settings = await db.Settings.SingleAsync(Ct);
        InstallationSettings defaults = InstallationSettings.Default();
        settings.Id.ShouldBe(1);
        settings.CycleInterval.ShouldBe(defaults.CycleInterval);
        settings.FailureLimit.ShouldBe(defaults.FailureLimit);
        settings.DefaultTimeLimit.ShouldBe(defaults.DefaultTimeLimit);
        settings.DefaultGracePeriod.ShouldBe(defaults.DefaultGracePeriod);
        settings.LogRetentionDays.ShouldBe(defaults.LogRetentionDays);
        settings.HistoryRetentionDays.ShouldBe(defaults.HistoryRetentionDays);
        settings.LogLevel.ShouldBe(defaults.LogLevel);
        settings.Email.ShouldBe(defaults.Email);
        settings.CheckForUpdates.ShouldBe(defaults.CheckForUpdates);
        settings.IsPaused.ShouldBeFalse();
        logLevel.Minimum.ShouldBe(MelLogLevel.Information);
    }

    [Fact]
    public async Task Initialize_Twice_KeepsChangedSettings()
    {
        await InitializeDatabaseAsync();
        await using (var db = NewContext())
        {
            InstallationSettings tracked = await db.Settings.SingleAsync(Ct);
            tracked.Change(
                TimeSpan.FromSeconds(30), 9, null, TimeSpan.FromSeconds(30), 30, 90, LogLevel.Warning, true);
            await db.SaveChangesAsync(Ct);
        }

        var logLevel = new LogLevelSwitch();
        await InitializeDatabaseAsync(logLevel);

        await using var reader = NewContext();
        InstallationSettings settings = await reader.Settings.SingleAsync(Ct);
        settings.FailureLimit.ShouldBe(9);
        settings.LogLevel.ShouldBe(LogLevel.Warning);
        logLevel.Minimum.ShouldBe(MelLogLevel.Warning);
    }

    [Fact]
    public async Task Initialize_NewDatabase_SetsJournalModeWal()
    {
        await InitializeDatabaseAsync();

        await using var connection = new SqliteConnection(GapActivatorDatabase.ConnectionString(Paths));
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        (await command.ExecuteScalarAsync(Ct)).ShouldBe("wal");
    }
}
