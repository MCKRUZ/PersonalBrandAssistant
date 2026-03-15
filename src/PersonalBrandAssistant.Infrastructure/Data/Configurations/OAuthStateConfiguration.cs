using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class OAuthStateConfiguration : IEntityTypeConfiguration<OAuthState>
{
    public void Configure(EntityTypeBuilder<OAuthState> builder)
    {
        builder.ToTable("OAuthStates");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.State).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Platform).IsRequired();
        builder.Property(o => o.EncryptedCodeVerifier);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.ExpiresAt).IsRequired();

        builder.HasIndex(o => o.State).IsUnique();
        builder.HasIndex(o => o.ExpiresAt);

        builder.Ignore(o => o.DomainEvents);
    }
}
