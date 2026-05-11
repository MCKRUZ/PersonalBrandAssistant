using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class ContentPlatformPublishConfiguration : IEntityTypeConfiguration<ContentPlatformPublish>
{
    public void Configure(EntityTypeBuilder<ContentPlatformPublish> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.PublishedUrl).HasMaxLength(2000);
        builder.Property(c => c.PlatformPostId).HasMaxLength(500);
        builder.Property(c => c.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(c => new { c.Platform, c.Status });
    }
}
