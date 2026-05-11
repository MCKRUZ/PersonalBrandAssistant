using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class ContentConfiguration : IEntityTypeConfiguration<Content>
{
    public void Configure(EntityTypeBuilder<Content> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).IsRequired().HasMaxLength(500);
        builder.Property(c => c.Body).HasColumnType("text");
        builder.Property(c => c.Tags).HasColumnType("jsonb");
        builder.Property(c => c.VoiceScore).HasPrecision(5, 2);
        builder.Property(c => c.ViralityPrediction).HasPrecision(5, 2);

        builder.HasOne(c => c.SourceIdea)
            .WithMany()
            .HasForeignKey(c => c.SourceIdeaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(c => c.HangfireJobId).HasMaxLength(200);

        builder.HasOne(c => c.ParentContent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.CrossPosts)
            .WithOne(p => p.Content)
            .HasForeignKey(p => p.ContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
