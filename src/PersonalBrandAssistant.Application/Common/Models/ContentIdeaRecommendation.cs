using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record PlatformFormatOption(
    PlatformType Platform,
    ContentType Format,
    string SuggestedAngle,
    string Rationale,
    float ConfidenceScore);

public record ContentIdeaRecommendation(
    string StoryTitle,
    string StorySummary,
    string? SourceUrl,
    IReadOnlyList<string> Angles,
    IReadOnlyList<PlatformFormatOption> Recommendations);

public record AnalyzeStoryRequest(
    string StoryText,
    string? SourceUrl = null);
