using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

public class UserFavoriteConfiguration : IEntityTypeConfiguration<UserFavoriteEntity>
{
    public void Configure(EntityTypeBuilder<UserFavoriteEntity> builder)
    {
        builder.ToTable("UserFavorites");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.UserId)
            .HasMaxLength(100);

        builder.Property(e => e.ActionId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.DisplayOrder)
            .IsRequired();

        // Relationship with Action
        builder.HasOne(e => e.Action)
            .WithMany()
            .HasForeignKey(e => e.ActionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for common queries
        builder.HasIndex(e => e.ActionId)
            .HasDatabaseName("IX_UserFavorites_ActionId");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_UserFavorites_UserId");

        builder.HasIndex(e => e.DisplayOrder)
            .HasDatabaseName("IX_UserFavorites_DisplayOrder");

        // Unique constraint to prevent duplicate favorites
        builder.HasIndex(e => new { e.UserId, e.ActionId })
            .IsUnique()
            .HasDatabaseName("IX_UserFavorites_UserId_ActionId");
    }
}
