using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class SubstackDetectionConfiguration : IEntityTypeConfiguration<SubstackDetection>
{
    public void Configure(EntityTypeBuilder<SubstackDetection> builder)
    {
        builder.ToTable("SubstackDetections");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RssGuid).IsRequired().HasMaxLength(500);
        builder.HasIndex(s => s.RssGuid).IsUnique();

        builder.Property(s => s.Title).IsRequired().HasMaxLength(500);

        builder.Property(s => s.SubstackUrl).IsRequired().HasMaxLength(2000);
        builder.HasIndex(s => s.SubstackUrl).IsUnique();

        builder.Property(s => s.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Confidence).IsRequired();

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(s => s.ContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(s => s.DomainEvents);
    }
}
