using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

/// <summary>
/// EF Core configuration for AuditLogEntity
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntity>
{
    public void Configure(EntityTypeBuilder<AuditLogEntity> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Timestamp)
            .IsRequired();

        builder.Property(e => e.ActionId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Command)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(e => e.ActionTitle)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(100);

        // Indexes for faster queries
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => new { e.ActionId, e.Timestamp });
        builder.HasIndex(e => e.Success);
    }
}
