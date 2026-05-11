using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class BrandProfileConfiguration : IEntityTypeConfiguration<BrandProfile>
{
    public void Configure(EntityTypeBuilder<BrandProfile> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Personality).HasColumnType("text");
        builder.Property(b => b.Tone).HasMaxLength(500);
        builder.Property(b => b.Topics).HasColumnType("jsonb");
        builder.Property(b => b.Vocabulary).HasColumnType("jsonb");
        builder.Property(b => b.AvoidWords).HasColumnType("jsonb");
        builder.Property(b => b.ExamplePosts).HasColumnType("text");
        builder.Property(b => b.LearningLog).HasColumnType("text");

        builder.HasData(new BrandProfile
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Personality = string.Empty,
            Tone = string.Empty,
            UpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
    }
}
