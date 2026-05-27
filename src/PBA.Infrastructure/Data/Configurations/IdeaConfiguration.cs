using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class IdeaConfiguration : IEntityTypeConfiguration<Idea>
{
    public void Configure(EntityTypeBuilder<Idea> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Title).IsRequired().HasMaxLength(500);
        builder.Property(i => i.Description).HasColumnType("text");
        builder.Property(i => i.Url).HasMaxLength(2000);
        builder.Property(i => i.SourceName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ThumbnailUrl).HasMaxLength(2000);
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.Property(i => i.Summary).HasColumnType("text");
        builder.Property(i => i.AIConnections).HasColumnType("text");
        builder.Property(i => i.Tags).HasColumnType("jsonb");
        builder.Property(i => i.DeduplicationKey).IsRequired().HasMaxLength(500);

        builder.HasOne(i => i.IdeaSource)
            .WithMany(s => (ICollection<Idea>)s.Ideas)
            .HasForeignKey(i => i.IdeaSourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(i => i.IdeaSourceId);
        builder.HasIndex(i => i.DeduplicationKey);
    }
}
