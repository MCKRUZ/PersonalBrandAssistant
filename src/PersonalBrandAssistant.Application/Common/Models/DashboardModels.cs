using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>KPI summary with current vs previous period comparison.</summary>
public record DashboardSummary(
    int TotalEngagement, int PreviousEngagement,
    int TotalImpressions, int PreviousImpressions,
    decimal EngagementRate, decimal PreviousEngagementRate,
    int ContentPublished, int PreviousContentPublished,
    decimal CostPerEngagement, decimal PreviousCostPerEngagement,
    int WebsiteUsers, int PreviousWebsiteUsers,
    DateTimeOffset GeneratedAt);

/// <summary>Daily engagement totals broken down by platform.</summary>
public record DailyEngagement(
    DateOnly Date,
    IReadOnlyList<PlatformDailyMetrics> Platforms,
    int Total);

/// <summary>Per-platform daily breakdown of likes/comments/shares.</summary>
public record PlatformDailyMetrics(
    PlatformType Platform, int Likes, int Comments, int Shares, int Total);
