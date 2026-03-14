using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class AutonomyConfigurationConfiguration : IEntityTypeConfiguration<AutonomyConfiguration>
{
    public void Configure(EntityTypeBuilder<AutonomyConfiguration> builder)
    {
        builder.ToTable("AutonomyConfigurations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.GlobalLevel).IsRequired();

        builder.Property(c => c.ContentTypeOverrides)
            .HasConversion(new JsonValueConverter<List<ContentTypeOverride>>())
            .HasColumnType("jsonb");

        builder.Property(c => c.PlatformOverrides)
            .HasConversion(new JsonValueConverter<List<PlatformOverride>>())
            .HasColumnType("jsonb");

        builder.Property(c => c.ContentTypePlatformOverrides)
            .HasConversion(new JsonValueConverter<List<ContentTypePlatformOverride>>())
            .HasColumnType("jsonb");

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(c => c.DomainEvents);
    }
}
