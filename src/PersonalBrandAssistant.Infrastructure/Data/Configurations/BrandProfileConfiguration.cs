using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class BrandProfileConfiguration : IEntityTypeConfiguration<BrandProfile>
{
    public void Configure(EntityTypeBuilder<BrandProfile> builder)
    {
        builder.ToTable("BrandProfiles");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).IsRequired().HasMaxLength(200);
        builder.Property(b => b.PersonaDescription).HasMaxLength(2000);
        builder.Property(b => b.StyleGuidelines).HasMaxLength(4000);

        builder.Property(b => b.VocabularyPreferences)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.VocabularyConfig>())
            .HasColumnType("jsonb");

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(b => b.DomainEvents);
    }
}
