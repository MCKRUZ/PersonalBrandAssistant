using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>Per-platform health summary for the dashboard.</summary>
public record PlatformSummary(
    PlatformType Platform, int? FollowerCount, int PostCount,
    double AvgEngagement, string? TopPostTitle, string? TopPostUrl,
    bool IsAvailable);
