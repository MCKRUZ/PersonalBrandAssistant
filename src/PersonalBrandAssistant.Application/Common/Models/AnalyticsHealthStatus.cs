namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>Connectivity status for an external analytics data source.</summary>
public record AnalyticsHealthStatus(
    string Source,
    bool IsHealthy,
    string? LastError,
    DateTimeOffset? LastSuccessAt);
