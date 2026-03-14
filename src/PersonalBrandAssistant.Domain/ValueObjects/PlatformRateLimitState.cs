namespace PersonalBrandAssistant.Domain.ValueObjects;

public class PlatformRateLimitState
{
    public int? RemainingCalls { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
    public TimeSpan? WindowDuration { get; set; }
    public Dictionary<string, EndpointRateLimit> Endpoints { get; set; } = new();
    public int? DailyQuotaUsed { get; set; }
    public int? DailyQuotaLimit { get; set; }
    public DateTimeOffset? QuotaResetAt { get; set; }
}

public class EndpointRateLimit
{
    public int? RemainingCalls { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
}
