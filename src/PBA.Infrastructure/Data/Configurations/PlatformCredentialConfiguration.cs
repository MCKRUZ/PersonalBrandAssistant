using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class PlatformCredentialConfiguration : IEntityTypeConfiguration<PlatformCredential>
{
    public void Configure(EntityTypeBuilder<PlatformCredential> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EncryptedAccessToken).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.EncryptedRefreshToken).HasMaxLength(4000);
        builder.Property(c => c.EncryptedCookies).HasMaxLength(8000);
        builder.Property(c => c.EncryptedIntegrationToken).HasMaxLength(4000);
        builder.Property(c => c.Scopes).HasMaxLength(1000);

        builder.Property(c => c.Purpose)
            .HasConversion<int>();

        builder.HasIndex(c => new { c.Platform, c.IsActive });

        // One active credential per (Platform, Purpose): a Publishing token and an Analytics token can
        // coexist for the same platform, but never two active credentials of the same purpose. Replaces
        // the old single-column unique index on Platform alone.
        builder.HasIndex(c => new { c.Platform, c.Purpose })
            .IsUnique()
            .HasFilter("\"IsActive\" = true");
    }
}
