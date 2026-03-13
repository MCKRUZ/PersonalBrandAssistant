using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class ContentConfiguration : IEntityTypeConfiguration<Content>
{
    public void Configure(EntityTypeBuilder<Content> builder)
    {
        builder.ToTable("Contents");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(500);
        builder.Property(c => c.ContentType).IsRequired();
        builder.Property(c => c.Status).IsRequired();

        builder.Property(c => c.Metadata)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.ContentMetadata>())
            .HasColumnType("jsonb");

        builder.Property(c => c.TargetPlatforms)
            .HasColumnType("integer[]");

        builder.Property(c => c.CapturedAutonomyLevel).IsRequired().HasDefaultValue(AutonomyLevel.Manual);
        builder.Property(c => c.RetryCount).IsRequired().HasDefaultValue(0);
        builder.Property(c => c.NextRetryAt);
        builder.Property(c => c.PublishingStartedAt);

        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.ScheduledAt);
        builder.HasIndex(c => new { c.Status, c.ScheduledAt });
        builder.HasIndex(c => new { c.Status, c.NextRetryAt });

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasQueryFilter(c => c.Status != ContentStatus.Archived);

        builder.Property(c => c.ParentContentId);
        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(c => c.ParentContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(c => c.DomainEvents);
    }
}
