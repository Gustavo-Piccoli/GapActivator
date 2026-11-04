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

    [Fact]
    public void Migrations_Runs_HaveAnIndexOnProcessIdAndEndedAt()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new GapActivatorDbContext(
            new DbContextOptionsBuilder<GapActivatorDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();

        // LastCompletedRunsAsync looks for a later run of the same process with a correlated NOT EXISTS.
        var names = new List<string>();
        using (SqliteCommand list = connection.CreateCommand())
        {
            list.CommandText = "PRAGMA index_list('Runs')";
            using SqliteDataReader reader = list.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(1));
            }
        }

        var columnSets = new List<string>();
        foreach (string name in names)
        {
            using SqliteCommand info = connection.CreateCommand();
            info.CommandText = $"PRAGMA index_info('{name}')";
            using SqliteDataReader reader = info.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                columns.Add(reader.GetString(2));
            }

            columnSets.Add(string.Join(",", columns));
        }

        columnSets.ShouldContain("ProcessId,EndedAt");
    }
}
