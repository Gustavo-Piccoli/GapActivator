using GapActivator.Infrastructure;
using GapActivator.Infrastructure.Logging;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Testing;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Tests.Support;

/// <summary>
/// Base class for tests over a real SQLite file in a <see cref="TempDataRoot"/>. Disposal releases the pooled
/// connections first, so the temporary folder can be deleted.
/// </summary>
public abstract class SqliteTestBase : IAsyncDisposable
{
    protected SqliteTestBase()
    {
        Paths = new GapActivatorPaths(Root.Path);
    }

    protected TempDataRoot Root { get; } = new();

    protected GapActivatorPaths Paths { get; set; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected GapActivatorDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GapActivatorDbContext>()
            .UseSqlite(GapActivatorDatabase.ConnectionString(Paths))
            .Options);

    protected async Task InitializeDatabaseAsync(LogLevelSwitch? logLevel = null)
    {
        await using var db = NewContext();
        await new DatabaseInitializer(db, Paths, logLevel ?? new LogLevelSwitch()).InitializeAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        GapActivatorDatabase.ReleasePooledConnections();
        await Root.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
