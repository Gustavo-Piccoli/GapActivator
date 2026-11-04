using Microsoft.Data.Sqlite;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>How GapActivator opens its SQLite database file.</summary>
public static class GapActivatorDatabase
{
    public static string ConnectionString(GapActivatorPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Closes the idle pooled connections, so a test can delete the database folder.</summary>
    public static void ReleasePooledConnections() => SqliteConnection.ClearAllPools();
}
