using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to build the model; adding a migration never opens the database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GapActivatorDbContext>
{
    public GapActivatorDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GapActivatorDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
