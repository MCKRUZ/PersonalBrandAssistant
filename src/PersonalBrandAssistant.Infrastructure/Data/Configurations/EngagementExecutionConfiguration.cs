using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class EngagementExecutionConfiguration : IEntityTypeConfiguration<EngagementExecution>
{
    public void Configure(EntityTypeBuilder<EngagementExecution> builder)
    {
        builder.ToTable("EngagementExecutions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ExecutedAt).IsRequired();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(e => e.ExecutedAt);

        builder.HasMany(e => e.Actions)
            .WithOne(a => a.EngagementExecution)
            .HasForeignKey(a => a.EngagementExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(e => e.DomainEvents);
    }
}
