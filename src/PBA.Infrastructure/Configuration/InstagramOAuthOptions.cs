namespace PBA.Infrastructure.Configuration;

public sealed class InstagramOAuthOptions
{
    public const string SectionName = "Publishing:Instagram";

    public bool Enabled { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
}
