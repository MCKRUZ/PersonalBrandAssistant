using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class AgentExecutionConfiguration : IEntityTypeConfiguration<AgentExecution>
{
    public void Configure(EntityTypeBuilder<AgentExecution> builder)
    {
        builder.ToTable("AgentExecutions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.AgentType).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.ModelUsed).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.Cost).IsRequired().HasPrecision(18, 6);

        builder.Property(e => e.ModelId).HasMaxLength(100);
        builder.Property(e => e.Error).HasMaxLength(4000);
        builder.Property(e => e.OutputSummary).HasMaxLength(2000);

        builder.Property(e => e.InputTokens).HasDefaultValue(0);
        builder.Property(e => e.OutputTokens).HasDefaultValue(0);
        builder.Property(e => e.CacheReadTokens).HasDefaultValue(0);
        builder.Property(e => e.CacheCreationTokens).HasDefaultValue(0);

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(e => e.ContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.Status, e.AgentType });
        builder.HasIndex(e => e.ContentId);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(e => e.DomainEvents);
    }
}
