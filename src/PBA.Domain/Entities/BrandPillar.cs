namespace PBA.Domain.Entities;

/// <summary>
/// A weighted brand pillar the feed ranker scores ideas against. Identity is <see cref="Id"/>
/// (sub-scores are keyed by it, R-C3); the name is display-only and may change without breaking
/// stored sub-scores. The description is what both the LLM analyzer and the embedding pre-filter
/// score against. <see cref="DescriptionEmbedding"/> stores the pillar vector; it maps to a
/// vector(1536) column via an EF value converter (Domain stays free of the Npgsql/pgvector deps).
/// </summary>
public class BrandPillar
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BrandRankingProfileId { get; init; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public double Weight { get; set; }              // 0..1; weights across pillars sum ~1.0 (query-time)
    public int Order { get; set; }
    public float[]? DescriptionEmbedding { get; set; } // vector(1536); populated by the embedding service
}
