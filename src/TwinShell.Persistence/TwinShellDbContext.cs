using Microsoft.EntityFrameworkCore;
using TwinShell.Persistence.Configurations;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence;

/// <summary>
/// Database context for TwinShell
/// </summary>
public sealed class TwinShellDbContext : DbContext
{
    public TwinShellDbContext(DbContextOptions<TwinShellDbContext> options)
        : base(options)
    {
    }

    public DbSet<ActionEntity> Actions => Set<ActionEntity>();
    public DbSet<CommandTemplateEntity> CommandTemplates => Set<CommandTemplateEntity>();
    public DbSet<CommandHistoryEntity> CommandHistories => Set<CommandHistoryEntity>();
    public DbSet<UserFavoriteEntity> UserFavorites => Set<UserFavoriteEntity>();
    public DbSet<CustomCategoryEntity> CustomCategories => Set<CustomCategoryEntity>();
    public DbSet<ActionCategoryMappingEntity> ActionCategoryMappings => Set<ActionCategoryMappingEntity>();
    public DbSet<ActionTranslationEntity> ActionTranslations => Set<ActionTranslationEntity>();
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
        modelBuilder.ApplyConfiguration(new CommandBatchConfiguration());
        modelBuilder.ApplyConfiguration(new SearchHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new SyncHistoryConfiguration());
    }
}
