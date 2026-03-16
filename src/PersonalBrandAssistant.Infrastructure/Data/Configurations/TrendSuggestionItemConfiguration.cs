using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class TrendSuggestionItemConfiguration : IEntityTypeConfiguration<TrendSuggestionItem>
{
    public void Configure(EntityTypeBuilder<TrendSuggestionItem> builder)
    {
        builder.ToTable("TrendSuggestionItems");

        builder.HasKey(si => new { si.TrendSuggestionId, si.TrendItemId });

        builder.HasOne<TrendItem>()
            .WithMany()
            .HasForeignKey(si => si.TrendItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
