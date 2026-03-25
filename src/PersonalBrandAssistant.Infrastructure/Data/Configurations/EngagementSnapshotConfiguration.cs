using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class EngagementSnapshotConfiguration : IEntityTypeConfiguration<EngagementSnapshot>
{
    public void Configure(EntityTypeBuilder<EngagementSnapshot> builder)
    {
        builder.ToTable("EngagementSnapshots");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ContentPlatformStatusId).IsRequired();
        builder.Property(e => e.Likes).IsRequired();
        builder.Property(e => e.Comments).IsRequired();
        builder.Property(e => e.Shares).IsRequired();
        builder.Property(e => e.FetchedAt).IsRequired();

        builder.HasIndex(e => new { e.ContentPlatformStatusId, e.FetchedAt })
            .IsDescending(false, true);

        // Reverse-order index for dashboard timeline queries that filter by FetchedAt first
        builder.HasIndex(e => new { e.FetchedAt, e.ContentPlatformStatusId });

        builder.HasOne<ContentPlatformStatus>()
            .WithMany()
            .HasForeignKey(e => e.ContentPlatformStatusId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(e => e.DomainEvents);
    }
}
