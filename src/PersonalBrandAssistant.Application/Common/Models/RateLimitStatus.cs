namespace PersonalBrandAssistant.Application.Common.Models;

public record RateLimitStatus(int? RemainingCalls, DateTimeOffset? ResetAt, bool IsLimited);
