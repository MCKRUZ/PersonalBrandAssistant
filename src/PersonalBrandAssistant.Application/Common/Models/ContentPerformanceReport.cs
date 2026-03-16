using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record ContentPerformanceReport(
    Guid ContentId,
    IReadOnlyDictionary<PlatformType, EngagementSnapshot> LatestByPlatform,
    int TotalEngagement,
    decimal? LlmCost,
    decimal? CostPerEngagement);
