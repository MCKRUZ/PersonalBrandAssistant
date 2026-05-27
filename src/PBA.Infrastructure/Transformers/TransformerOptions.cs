namespace PBA.Infrastructure.Transformers;

public sealed class TransformerOptions
{
    public const string SectionName = "ContentTransformer";

    public string BaseUrl { get; init; } = "https://matthewkruczek.ai";
}
