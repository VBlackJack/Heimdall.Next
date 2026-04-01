using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TwinShell.Persistence;

/// <summary>
/// Factory for creating DbContext instances at design time (for EF Core migrations).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TwinShellDbContext>
{
    public TwinShellDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TwinShellDbContext>();

        // Use a temporary SQLite database for migration generation
        optionsBuilder.UseSqlite("Data Source=:memory:");

        return new TwinShellDbContext(optionsBuilder.Options);
    }
}
