namespace PBA.Infrastructure.Configuration;

public sealed class TikTokOAuthOptions
{
    public const string SectionName = "Publishing:TikTok";

    public bool Enabled { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
}
