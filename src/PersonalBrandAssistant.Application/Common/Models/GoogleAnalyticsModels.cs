namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>GA4 overview metrics for a date range.</summary>
public record WebsiteOverview(
    int ActiveUsers, int Sessions, int PageViews,
    double AvgSessionDuration, double BounceRate, int NewUsers);

/// <summary>GA4 page-level metrics.</summary>
public record PageViewEntry(string PagePath, int Views, int Users);

/// <summary>GA4 traffic source breakdown.</summary>
public record TrafficSourceEntry(string Channel, int Sessions, int Users);

/// <summary>Search Console query performance.</summary>
public record SearchQueryEntry(
    string Query, int Clicks, int Impressions, double Ctr, double Position);
