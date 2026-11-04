using GapActivator.Domain.Runs;
using GapActivator.Infrastructure.Files;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Infrastructure.Tests.Files;

[Trait("Category", "Integration")]
public sealed class FileLogRetentionTests : IAsyncLifetime
{
    private readonly TempDataRoot _root = new();
    private readonly LogRecorder _logs = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2030, 6, 30, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GapActivatorPaths Paths => new(_root.Path);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    // BI-087: retention by age purges old service logs and run outputs.
    [Fact]
    public async Task Purge_OldServiceLogsAndRunOutputs_AreDeletedRecentAndUnrelatedKept()
    {
        Paths.EnsureDirectories();
        var oldService = Touch(Path.Combine(Paths.LogsDirectory, "gapactivator-20300101.log"), 40);
        var recentService = Touch(Path.Combine(Paths.LogsDirectory, "gapactivator-20300625.log"), 5);
        var oldRun = Touch(Paths.RunOutputFile(RunId.New()), 31);
        var recentRun = Touch(Paths.RunOutputFile(RunId.New()), 29);
        var notes = Touch(Path.Combine(Paths.LogsDirectory, "notes.txt"), 400);
        var database = Touch(Paths.DatabaseFile, 400);
        var otherLog = Touch(Path.Combine(Paths.LogsDirectory, "other.log"), 400);
        var runNote = Touch(Path.Combine(Paths.RunLogsDirectory, "readme.txt"), 400);

        await Create().PurgeOlderThanAsync(30, Ct);

        Assert.False(File.Exists(oldService));
        Assert.False(File.Exists(oldRun));
        Assert.True(File.Exists(recentService));
        Assert.True(File.Exists(recentRun));
        Assert.True(File.Exists(notes));
        Assert.True(File.Exists(database));
        Assert.True(File.Exists(otherLog));
        Assert.True(File.Exists(runNote));
    }

    [Fact]
    public async Task Purge_FileOpenByARunningChild_IsSkippedOthersDeleted()
    {
        Paths.EnsureDirectories();
        var busy = Touch(Paths.RunOutputFile(RunId.New()), 60);
        var idle = Touch(Paths.RunOutputFile(RunId.New()), 60);
        var busyService = Touch(Path.Combine(Paths.LogsDirectory, "gapactivator-20300101.log"), 60);
        await using var running = new FileStream(busy, FileMode.Open, FileAccess.Write, FileShare.Read);
        await using var serviceLog = new FileStream(busyService, FileMode.Open, FileAccess.Write, FileShare.Read);

        await Create().PurgeOlderThanAsync(30, Ct);

        Assert.True(File.Exists(busy));
        Assert.True(File.Exists(busyService));
        Assert.False(File.Exists(idle));
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task Purge_MissingFolders_DoesNothing()
    {
        await Create().PurgeOlderThanAsync(30, Ct);

        Assert.False(Directory.Exists(Paths.LogsDirectory));
    }

    [Fact]
    public async Task Purge_CancelledToken_Throws()
    {
        Paths.EnsureDirectories();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().PurgeOlderThanAsync(30, cts.Token));
    }

    private FileLogRetention Create() => new(Paths, _time, new RecordingLogger<FileLogRetention>(_logs));

    private string Touch(string path, int ageDays)
    {
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime.AddDays(-ageDays));
        return path;
    }
}
