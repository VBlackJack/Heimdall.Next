using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

public class SearchHistoryConfiguration : IEntityTypeConfiguration<SearchHistoryEntity>
{
    public void Configure(EntityTypeBuilder<SearchHistoryEntity> builder)
    {
        builder.ToTable("SearchHistories");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.SearchTerm)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.NormalizedSearchTerm)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.SearchCount)
            .IsRequired();

        builder.Property(e => e.ResultCount)
            .IsRequired();

        builder.Property(e => e.LastSearchedAt)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.WasSuccessful)
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasMaxLength(100);

        // Indexes for common queries
        builder.HasIndex(e => e.LastSearchedAt)
            .HasDatabaseName("IX_SearchHistories_LastSearchedAt");

        builder.HasIndex(e => e.SearchCount)
            .HasDatabaseName("IX_SearchHistories_SearchCount");

        builder.HasIndex(e => e.NormalizedSearchTerm)
            .HasDatabaseName("IX_SearchHistories_NormalizedSearchTerm");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_SearchHistories_UserId");

        // Unique index for normalized search term per user (prevent duplicates)
        builder.HasIndex(e => new { e.NormalizedSearchTerm, e.UserId })
            .IsUnique()
            .HasDatabaseName("IX_SearchHistories_NormalizedSearchTerm_UserId");
    }
}
