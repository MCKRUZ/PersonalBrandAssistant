namespace PersonalBrandAssistant.Domain.ValueObjects;

public class PlatformRateLimitState
{
    public int? RemainingCalls { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
    public TimeSpan? WindowDuration { get; set; }
}
