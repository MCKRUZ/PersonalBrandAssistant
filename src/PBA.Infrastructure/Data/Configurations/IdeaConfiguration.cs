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

        builder.Property(i => i.ScoreReason).HasColumnType("text");

        builder.HasIndex(i => i.ScoredAt);
        builder.HasIndex(i => i.Score);
        builder.HasIndex(i => i.DuplicateOfId);
        builder.HasIndex(i => i.AlertedAt);

        builder.HasOne<Idea>()
            .WithMany()
            .HasForeignKey(i => i.DuplicateOfId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
