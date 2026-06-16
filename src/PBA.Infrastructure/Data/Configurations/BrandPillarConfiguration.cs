using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class BrandPillarConfiguration : IEntityTypeConfiguration<BrandPillar>
{
    public void Configure(EntityTypeBuilder<BrandPillar> builder)
    {
        builder.ToTable("BrandPillars");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Description).HasColumnType("text");

        // DescriptionEmbedding maps to vector(1536) via a float[]<->Vector converter, applied only
        // under the Npgsql provider (PgVectorModelConfiguration) — the InMemory test provider can't
        // map the Vector provider type, and stores the float[] natively instead.
    }
}
