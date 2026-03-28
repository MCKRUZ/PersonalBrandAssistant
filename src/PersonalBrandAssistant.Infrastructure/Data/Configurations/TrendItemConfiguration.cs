using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class TrendItemConfiguration : IEntityTypeConfiguration<TrendItem>
{
    public void Configure(EntityTypeBuilder<TrendItem> builder)
    {
        builder.ToTable("TrendItems");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.Url).HasMaxLength(2000);
        builder.Property(t => t.SourceName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.SourceType).IsRequired();
        builder.Property(t => t.DetectedAt).IsRequired();
        builder.Property(t => t.ThumbnailUrl).HasMaxLength(500);
        builder.Property(t => t.DeduplicationKey).HasMaxLength(128);
        builder.Property(t => t.Category).HasMaxLength(50);
        builder.Property(t => t.Summary).HasColumnType("text");

        builder.HasIndex(t => t.DeduplicationKey)
            .IsUnique()
            .HasFilter("\"DeduplicationKey\" IS NOT NULL");
        builder.HasIndex(t => t.DetectedAt);

        builder.HasOne<TrendSource>()
            .WithMany()
            .HasForeignKey(t => t.TrendSourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(t => t.DomainEvents);
    }
}
