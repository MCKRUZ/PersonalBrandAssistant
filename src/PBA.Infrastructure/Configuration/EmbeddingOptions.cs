namespace PBA.Infrastructure.Configuration;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string Model { get; init; } = "openai/text-embedding-3-small";
    public int Dimensions { get; init; } = 1536;
    public int BatchSize { get; init; } = 128;

    // Per-input character cap. text-embedding-3-small accepts 8191 tokens per input; an over-long idea
    // (e.g. a full article body) makes the provider return 0 vectors and fails the WHOLE batch (count
    // mismatch), leaving every co-batched idea unembedded. 20k chars (~5-8k tokens) stays under the limit
    // while preserving enough text for brand-fit — embeddings only need the topical gist.
    public int MaxInputChars { get; init; } = 20000;
}
