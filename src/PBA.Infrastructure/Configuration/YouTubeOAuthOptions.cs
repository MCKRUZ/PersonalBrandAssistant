namespace PBA.Infrastructure.Configuration;

// SectionName keeps the Publishing: prefix (it names the app registration, not the token purpose).
public sealed class YouTubeOAuthOptions
{
    public const string SectionName = "Publishing:YouTube";

    public bool Enabled { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
    public string? ApiKey { get; init; }
}
