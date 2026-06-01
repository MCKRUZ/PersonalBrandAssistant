namespace PBA.Infrastructure.Configuration;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string Model { get; init; } = "google/gemini-2.5-pro";
    public int MaxTokens { get; init; } = 4096;
    public int TimeoutMs { get; init; } = 180_000;
}
