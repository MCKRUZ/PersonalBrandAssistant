using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IRateLimiter
{
    Task<RateLimitDecision> CanMakeRequestAsync(PlatformType platform, string endpoint, CancellationToken ct);
    Task RecordRequestAsync(PlatformType platform, string endpoint, int remaining, DateTimeOffset? resetAt, CancellationToken ct);
    Task<RateLimitStatus> GetStatusAsync(PlatformType platform, CancellationToken ct);
}
