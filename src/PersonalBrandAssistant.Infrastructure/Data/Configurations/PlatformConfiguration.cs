using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class PlatformConfiguration : IEntityTypeConfiguration<Platform>
{
    public void Configure(EntityTypeBuilder<Platform> builder)
    {
        builder.ToTable("Platforms");

        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.Type).IsUnique();

        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.EncryptedAccessToken);
        builder.Property(p => p.EncryptedRefreshToken);

        builder.Property(p => p.RateLimitState)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.PlatformRateLimitState>())
            .HasColumnType("jsonb");
        builder.Property(p => p.Settings)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.PlatformSettings>())
            .HasColumnType("jsonb");

        builder.Property(p => p.GrantedScopes)
            .HasColumnType("text[]");

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(p => p.DomainEvents);
    }
}
