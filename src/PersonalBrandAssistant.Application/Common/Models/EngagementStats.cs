namespace PersonalBrandAssistant.Application.Common.Models;

public record EngagementStats(
    int Likes,
    int Comments,
    int Shares,
    int Impressions,
    int Clicks,
    IReadOnlyDictionary<string, int> PlatformSpecific);
