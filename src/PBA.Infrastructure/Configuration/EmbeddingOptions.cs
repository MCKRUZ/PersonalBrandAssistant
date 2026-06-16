namespace PBA.Infrastructure.Configuration;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string Model { get; init; } = "openai/text-embedding-3-small";
    public int Dimensions { get; init; } = 1536;
    public int BatchSize { get; init; } = 128;
}
