using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class HeldMediaConfiguration : IEntityTypeConfiguration<HeldMedia>
{
    public void Configure(EntityTypeBuilder<HeldMedia> builder)
    {
        // ContentId is the key: one clip per content record, and the row cannot outlive the content
        // it belongs to. Cascade delete matters here more than usual — an orphaned row is tens of
        // megabytes, not a few bytes.
        builder.HasKey(h => h.ContentId);

        builder.HasOne<Content>()
            .WithOne()
            .HasForeignKey<HeldMedia>(h => h.ContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(h => h.FileName).HasMaxLength(500);
        builder.Property(h => h.ContentType).HasMaxLength(200);
    }
}
