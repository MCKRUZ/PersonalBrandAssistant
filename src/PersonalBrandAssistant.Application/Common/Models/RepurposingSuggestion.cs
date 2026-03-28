using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record RepurposingSuggestion(
    PlatformType Platform,
    ContentType SuggestedType,
    string Rationale,
    float ConfidenceScore);
