using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IRateLimiter
{
    Task<Result<RateLimitDecision>> CanMakeRequestAsync(PlatformType platform, string endpoint, CancellationToken ct);
    Task<Result<bool>> RecordRequestAsync(PlatformType platform, string endpoint, int remaining, DateTimeOffset? resetAt, CancellationToken ct);
    Task<Result<RateLimitStatus>> GetStatusAsync(PlatformType platform, CancellationToken ct);
}
