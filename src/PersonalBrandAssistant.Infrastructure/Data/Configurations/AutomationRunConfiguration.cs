using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class AutomationRunConfiguration : IEntityTypeConfiguration<AutomationRun>
{
    public void Configure(EntityTypeBuilder<AutomationRun> builder)
    {
        builder.ToTable("AutomationRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.TriggeredAt).IsRequired();
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.ImageFileId).HasMaxLength(500);
        builder.Property(r => r.ImagePrompt).HasMaxLength(4000);
        builder.Property(r => r.SelectionReasoning).HasMaxLength(2000);
        builder.Property(r => r.ErrorDetails).HasMaxLength(4000);

        builder.HasIndex(r => r.TriggeredAt);
        builder.HasIndex(r => r.Status);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(r => r.DomainEvents);
    }
}
