using GapActivator.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Tests.Persistence;

/// <summary>Fast model checks over an in-memory SQLite connection (not an integration test).</summary>
public sealed class ModelTests
{
    [Fact]
    public void Model_NoChangesSinceMigration_HasNoPendingChanges()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new GapActivatorDbContext(
            new DbContextOptionsBuilder<GapActivatorDbContext>().UseSqlite(connection).Options);

        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
