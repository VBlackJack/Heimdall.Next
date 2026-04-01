using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

public class ActionCategoryMappingConfiguration : IEntityTypeConfiguration<ActionCategoryMappingEntity>
{
    public void Configure(EntityTypeBuilder<ActionCategoryMappingEntity> builder)
    {
        builder.ToTable("ActionCategoryMappings");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ActionId)
            .IsRequired();

        builder.Property(m => m.CategoryId)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        // Configure many-to-many relationship
        builder.HasOne(m => m.Action)
            .WithMany()
            .HasForeignKey(m => m.ActionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Category)
            .WithMany(c => c.ActionMappings)
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Prevent duplicate mappings
        builder.HasIndex(m => new { m.ActionId, m.CategoryId })
            .IsUnique();
    }
}
