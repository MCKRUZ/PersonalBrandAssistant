namespace PBA.Api.Authentication;

/// <summary>
/// Configuration for the external API surface (/api/external) consumed by trusted
/// server-to-server clients such as project-avatar. Bound from the "ExternalApi"
/// configuration section. The key is supplied via User Secrets (dev) or an
/// ExternalApi__ApiKey environment variable (prod) — never committed.
/// </summary>
public sealed class ExternalApiOptions
{
    public const string SectionName = "ExternalApi";

    public string ApiKey { get; init; } = string.Empty;
}
