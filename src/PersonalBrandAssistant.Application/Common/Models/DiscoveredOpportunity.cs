namespace PersonalBrandAssistant.Application.Common.Models;

public sealed record DiscoveredOpportunity(
    string PostId,
    string PostUrl,
    string Title,
    string ContentPreview,
    string Community,
    string Platform,
    DateTimeOffset DiscoveredAt,
    string ImpactScore = "Medium",
    string Category = "General");
