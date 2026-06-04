namespace PBA.Application.Features.Analytics.Dtos;

public sealed record WebsiteOverview(
    int ActiveUsers,
    int Sessions,
    int PageViews,
    double AvgSessionDuration,
    double BounceRate,
    int NewUsers);

public sealed record PageViewEntry(string PagePath, int Views, int UniqueUsers);

public sealed record TrafficSourceEntry(string Channel, int Sessions, int Users);

public sealed record SearchQueryEntry(string Query, int Clicks, int Impressions, double Ctr, double Position);

public sealed record WebsiteAnalyticsDto(
    WebsiteOverview Overview,
    IReadOnlyList<PageViewEntry> TopPages,
    IReadOnlyList<TrafficSourceEntry> TrafficSources,
    IReadOnlyList<SearchQueryEntry> SearchQueries);

public sealed record AnalyticsHealthDto(bool Ga4, bool SearchConsole);
