using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class BrandRankingProfileConfiguration : IEntityTypeConfiguration<BrandRankingProfile>
{
    public void Configure(EntityTypeBuilder<BrandRankingProfile> builder)
    {
        builder.ToTable("BrandRankingProfiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Positioning).HasColumnType("text");
        builder.Property(p => p.AudiencePrimary).HasColumnType("text");
        builder.Property(p => p.AudienceSecondary).HasColumnType("text");
        builder.Property(p => p.AuthorityTopics).HasColumnType("jsonb");
        builder.Property(p => p.AntiTopics).HasColumnType("jsonb");
        builder.Property(p => p.VoiceMarkers).HasColumnType("jsonb");

        builder.HasMany(p => p.Pillars)
            .WithOne()
            .HasForeignKey(pl => pl.BrandRankingProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optimistic concurrency via Npgsql's xmin system column (R-H3). xmin already exists on every
        // table, so it is mapped, not created — Npgsql recognizes the "xmin"/"xid" mapping as the
        // system column and emits no DDL column for it.
        builder.Property(p => p.Xmin)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Exactly one active profile (R-L4): partial unique index over IsActive WHERE IsActive = true.
        builder.HasIndex(p => p.IsActive)
            .IsUnique()
            .HasFilter("\"IsActive\" = true");

        // No HasData seed — seeding is an idempotent, race-safe runtime service (R-L5), not a data migration.
    }
}
