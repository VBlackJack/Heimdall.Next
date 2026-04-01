using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

/// <summary>
/// EF Core configuration for ActionTranslationEntity
/// </summary>
public class ActionTranslationConfiguration : IEntityTypeConfiguration<ActionTranslationEntity>
{
    public void Configure(EntityTypeBuilder<ActionTranslationEntity> builder)
    {
        builder.ToTable("ActionTranslations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActionId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.CultureCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.Notes)
            .HasMaxLength(2000);

        // Relationship with Action
        builder.HasOne(e => e.Action)
            .WithMany()
            .HasForeignKey(e => e.ActionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for faster lookups
        builder.HasIndex(e => new { e.ActionId, e.CultureCode })
            .IsUnique();
    }
}
