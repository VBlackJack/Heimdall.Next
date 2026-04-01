using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Core.Constants;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

public class CommandTemplateConfiguration : IEntityTypeConfiguration<CommandTemplateEntity>
{
    public void Configure(EntityTypeBuilder<CommandTemplateEntity> builder)
    {
        builder.ToTable("CommandTemplates");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(ValidationConstants.MaxTemplateNameLength); // 200

        builder.Property(e => e.CommandPattern)
            .IsRequired()
            .HasMaxLength(ValidationConstants.MaxTemplateCommandPatternLength); // 1000

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.ParametersJson)
            .IsRequired()
            .HasColumnType("TEXT");

        // PublicId for GitOps synchronization - must be unique
        builder.Property(e => e.PublicId)
            .IsRequired();

        builder.HasIndex(e => e.PublicId)
            .IsUnique()
            .HasDatabaseName("IX_CommandTemplates_PublicId");
    }
}
