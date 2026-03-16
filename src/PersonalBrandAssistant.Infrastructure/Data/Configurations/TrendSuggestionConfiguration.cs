using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class TrendSuggestionConfiguration : IEntityTypeConfiguration<TrendSuggestion>
{
    public void Configure(EntityTypeBuilder<TrendSuggestion> builder)
    {
        builder.ToTable("TrendSuggestions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Topic).IsRequired().HasMaxLength(500);
        builder.Property(s => s.Rationale).IsRequired().HasMaxLength(2000);
        builder.Property(s => s.RelevanceScore).IsRequired();
        builder.Property(s => s.SuggestedContentType).IsRequired();
        builder.Property(s => s.SuggestedPlatforms).HasColumnType("integer[]");
        builder.Property(s => s.Status).IsRequired().HasDefaultValue(TrendSuggestionStatus.Pending);

        builder.HasMany(s => s.RelatedTrends)
            .WithOne()
            .HasForeignKey(si => si.TrendSuggestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.Status);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(s => s.DomainEvents);
    }
}
