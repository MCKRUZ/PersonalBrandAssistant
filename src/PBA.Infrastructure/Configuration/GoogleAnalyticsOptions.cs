namespace PBA.Infrastructure.Configuration;

public sealed class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>GA4 numeric property id, e.g. "261358185".</summary>
    public string PropertyId { get; init; } = string.Empty;

    /// <summary>Search Console site URL, e.g. "https://matthewkruczek.ai/".</summary>
    public string SiteUrl { get; init; } = string.Empty;

    /// <summary>Path to the service-account JSON, relative to content root.</summary>
    public string CredentialsPath { get; init; } = "secrets/google-analytics-sa.json";
}
