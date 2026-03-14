namespace PersonalBrandAssistant.Application.Common.Models;

public record RateLimitDecision(bool Allowed, DateTimeOffset? RetryAt, string? Reason);
