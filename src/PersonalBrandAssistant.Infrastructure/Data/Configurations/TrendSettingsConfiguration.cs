using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class TrendSettingsConfiguration : IEntityTypeConfiguration<TrendSettings>
{
    public void Configure(EntityTypeBuilder<TrendSettings> builder)
    {
        builder.ToTable("TrendSettings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RelevanceFilterEnabled).IsRequired();
        builder.Property(s => s.RelevanceScoreThreshold).IsRequired();
        builder.Property(s => s.MaxSuggestionsPerCycle).IsRequired();

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(s => s.DomainEvents);
    }
}
