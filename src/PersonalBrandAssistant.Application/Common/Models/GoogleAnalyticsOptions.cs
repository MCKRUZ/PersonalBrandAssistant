namespace PersonalBrandAssistant.Application.Common.Models;

public class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>Path to the Google service account JSON key file.</summary>
    public string CredentialsPath { get; set; } = "secrets/google-analytics-sa.json";

    /// <summary>GA4 property ID (numeric).</summary>
    public string PropertyId { get; set; } = "261358185";

    /// <summary>Site URL for Search Console queries (include trailing slash).</summary>
    public string SiteUrl { get; set; } = "https://matthewkruczek.ai/";
}
