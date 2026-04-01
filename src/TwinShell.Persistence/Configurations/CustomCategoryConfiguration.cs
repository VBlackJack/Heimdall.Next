using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

public class CustomCategoryConfiguration : IEntityTypeConfiguration<CustomCategoryEntity>
{
    public void Configure(EntityTypeBuilder<CustomCategoryEntity> builder)
    {
        builder.ToTable("CustomCategories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.IconKey)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.ColorHex)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.IsSystemCategory)
            .IsRequired();

        builder.Property(c => c.DisplayOrder)
            .IsRequired();

        builder.Property(c => c.IsHidden)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasIndex(c => c.DisplayOrder);
        builder.HasIndex(c => c.Name);

        // PublicId for GitOps synchronization - must be unique
        builder.Property(c => c.PublicId)
            .IsRequired();

        builder.HasIndex(c => c.PublicId)
            .IsUnique()
            .HasDatabaseName("IX_CustomCategories_PublicId");
    }
}
