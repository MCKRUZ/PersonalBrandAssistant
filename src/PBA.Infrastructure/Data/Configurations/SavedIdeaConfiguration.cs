using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class SavedIdeaConfiguration : IEntityTypeConfiguration<SavedIdea>
{
    public void Configure(EntityTypeBuilder<SavedIdea> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasOne(s => s.Idea)
            .WithOne(i => i.SavedDetails)
            .HasForeignKey<SavedIdea>(s => s.IdeaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.IdeaId).IsUnique();

        builder.Property(s => s.Tags).HasColumnType("jsonb");
        builder.Property(s => s.SuggestedPlatforms).HasColumnType("jsonb");
    }
}
