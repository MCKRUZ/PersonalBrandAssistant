using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class ContentPlatformStatusConfiguration : IEntityTypeConfiguration<ContentPlatformStatus>
{
    public void Configure(EntityTypeBuilder<ContentPlatformStatus> builder)
    {
        builder.ToTable("ContentPlatformStatuses");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ContentId).IsRequired();
        builder.Property(c => c.Platform).IsRequired();
        builder.Property(c => c.Status).IsRequired().HasDefaultValue(PlatformPublishStatus.Pending);
        builder.Property(c => c.PlatformPostId).HasMaxLength(500);
        builder.Property(c => c.PostUrl).HasMaxLength(2000);
        builder.Property(c => c.ErrorMessage).HasMaxLength(4000);
        builder.Property(c => c.IdempotencyKey).HasMaxLength(200);
        builder.Property(c => c.RetryCount).IsRequired().HasDefaultValue(0);

        builder.HasIndex(c => new { c.ContentId, c.Platform });
        builder.HasIndex(c => c.IdempotencyKey).IsUnique();

        // Dashboard query index: count published content per platform in a date range
        builder.HasIndex(c => new { c.PublishedAt, c.Platform })
            .HasFilter("\"PublishedAt\" IS NOT NULL");

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(c => c.ContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(c => c.DomainEvents);
    }
}
