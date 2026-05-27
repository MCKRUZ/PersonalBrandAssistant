namespace PBA.Infrastructure.Configuration;

public sealed class TwitterOptions
{
    public const string SectionName = "Publishing:Twitter";

    public bool Enabled { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
    public string? ApiKey { get; init; }
    public string? ApiSecret { get; init; }
}
