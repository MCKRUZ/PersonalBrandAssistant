using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");

        builder.HasKey(a => a.Id);

        builder.HasIndex(a => a.Timestamp);

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(50);
        builder.Property(a => a.OldValue).HasColumnType("text");
        builder.Property(a => a.NewValue).HasColumnType("text");
        builder.Property(a => a.Details).HasMaxLength(2000);

        builder.Ignore(a => a.DomainEvents);
    }
}
