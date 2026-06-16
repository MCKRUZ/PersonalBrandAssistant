using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using Pgvector;

namespace PBA.Infrastructure.Data;

/// <summary>
/// pgvector-specific mappings applied only under the Npgsql provider. Entities expose embeddings as
/// <c>float[]?</c> (so the Domain stays free of the Npgsql/pgvector dependency); here they map to a
/// <c>vector(1536)</c> column via a float[]&lt;-&gt;Vector value converter. The InMemory test provider
/// can't map the <c>Vector</c> provider type, so this is skipped there and the float[] is stored natively.
/// No pgvector SQL is ever issued over these columns (all cosine math runs in memory), so a value
/// converter is safe.
/// </summary>
internal static class PgVectorModelConfiguration
{
    public const int Dimensions = 1536;

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<BrandPillar>()
            .Property(p => p.DescriptionEmbedding)
            .HasColumnType($"vector({Dimensions})")
            .HasConversion(
                v => v == null ? null : new Vector(v),
                v => v == null ? null : v.ToArray());
    }
}
