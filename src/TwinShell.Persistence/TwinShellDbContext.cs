using Microsoft.EntityFrameworkCore;
using TwinShell.Persistence.Configurations;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence;

/// <summary>
/// Database context for TwinShell
/// </summary>
public class TwinShellDbContext : DbContext
{
    // SECURITY: Whitelist of valid table names to prevent SQL injection
    private static readonly HashSet<string> AllowedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Actions",
        "CommandBatches",
        "CustomCategories",
        "CommandTemplates"
    };

    public TwinShellDbContext(DbContextOptions<TwinShellDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Ensures the GitOps schema migration is applied (PublicId columns and indexes).
    /// This method safely adds the PublicId column if it doesn't exist.
    /// </summary>
    public async Task EnsureGitOpsSchemaMigrationAsync()
    {
        var connection = Database.GetDbConnection();
        await connection.OpenAsync();

        try
        {
            // Check and add PublicId to Actions table
            await AddPublicIdColumnIfNotExistsAsync("Actions");

            // Check and add PublicId to CommandBatches table
            await AddPublicIdColumnIfNotExistsAsync("CommandBatches");

            // Check and add PublicId to CustomCategories table
            await AddPublicIdColumnIfNotExistsAsync("CustomCategories");

            // Check and add PublicId to CommandTemplates table
            await AddPublicIdColumnIfNotExistsAsync("CommandTemplates");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private async Task AddPublicIdColumnIfNotExistsAsync(string tableName)
    {
        // SECURITY: Validate table name against whitelist to prevent SQL injection
        if (!AllowedTableNames.Contains(tableName))
        {
            throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));
        }

        // Check if column exists using a direct query
        using var checkCommand = Database.GetDbConnection().CreateCommand();
        checkCommand.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = 'PublicId'";
        var result = await checkCommand.ExecuteScalarAsync();
        var exists = Convert.ToInt32(result) > 0;

        if (!exists)
        {
            // SQLite doesn't support non-constant DEFAULT values in ALTER TABLE
            // So we add the column with an empty default, then update all rows

            // Step 1: Add column with empty string default
            using var addCommand = Database.GetDbConnection().CreateCommand();
            addCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN PublicId TEXT NOT NULL DEFAULT ''";
            await addCommand.ExecuteNonQueryAsync();

            // Step 2: Update all existing rows with unique UUIDs
            // SQLite UUID generation using randomblob
            using var updateCommand = Database.GetDbConnection().CreateCommand();
            updateCommand.CommandText = $@"
                UPDATE {tableName}
                SET PublicId = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))";
            await updateCommand.ExecuteNonQueryAsync();

            // Step 3: Create unique index
            using var indexCommand = Database.GetDbConnection().CreateCommand();
            indexCommand.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS IX_{tableName}_PublicId ON {tableName} (PublicId)";
            await indexCommand.ExecuteNonQueryAsync();
        }
    }

    public DbSet<ActionEntity> Actions => Set<ActionEntity>();
    public DbSet<CommandTemplateEntity> CommandTemplates => Set<CommandTemplateEntity>();
    public DbSet<CommandHistoryEntity> CommandHistories => Set<CommandHistoryEntity>();
    public DbSet<UserFavoriteEntity> UserFavorites => Set<UserFavoriteEntity>();
    public DbSet<CustomCategoryEntity> CustomCategories => Set<CustomCategoryEntity>();
    public DbSet<ActionCategoryMappingEntity> ActionCategoryMappings => Set<ActionCategoryMappingEntity>();
    public DbSet<ActionTranslationEntity> ActionTranslations => Set<ActionTranslationEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<CommandBatchEntity> CommandBatches => Set<CommandBatchEntity>();
    public DbSet<SearchHistoryEntity> SearchHistories => Set<SearchHistoryEntity>();
    public DbSet<SyncHistoryEntity> SyncHistories => Set<SyncHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ActionConfiguration());
        modelBuilder.ApplyConfiguration(new CommandTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new CommandHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new UserFavoriteConfiguration());
        modelBuilder.ApplyConfiguration(new CustomCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ActionCategoryMappingConfiguration());
        modelBuilder.ApplyConfiguration(new ActionTranslationConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new CommandBatchConfiguration());
        modelBuilder.ApplyConfiguration(new SearchHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new SyncHistoryConfiguration());
    }
}
