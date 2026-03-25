using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record TopPerformingContent(
    Guid ContentId,
    string Title,
    int TotalEngagement,
    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform,
    int? Impressions = null,
    decimal? EngagementRate = null);
