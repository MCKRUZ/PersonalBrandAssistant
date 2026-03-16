using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class ContentSeriesConfiguration : IEntityTypeConfiguration<ContentSeries>
{
    public void Configure(EntityTypeBuilder<ContentSeries> builder)
    {
        builder.ToTable("ContentSeries");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.RecurrenceRule).IsRequired().HasMaxLength(500);
        builder.Property(s => s.TargetPlatforms).HasColumnType("integer[]");
        builder.Property(s => s.ContentType).IsRequired();
        builder.Property(s => s.ThemeTags)
            .HasConversion(new JsonValueConverter<List<string>>())
            .HasColumnType("jsonb");
        builder.Property(s => s.TimeZoneId).IsRequired().HasMaxLength(100);
        builder.Property(s => s.IsActive).IsRequired();

        builder.HasIndex(s => s.IsActive);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(s => s.DomainEvents);
    }
}
