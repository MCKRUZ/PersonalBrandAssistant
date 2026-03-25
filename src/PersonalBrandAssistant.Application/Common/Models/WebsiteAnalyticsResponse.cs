namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>Combined GA4 + Search Console response.</summary>
public record WebsiteAnalyticsResponse(
    WebsiteOverview Overview,
    IReadOnlyList<PageViewEntry> TopPages,
    IReadOnlyList<TrafficSourceEntry> TrafficSources,
    IReadOnlyList<SearchQueryEntry> SearchQueries);
