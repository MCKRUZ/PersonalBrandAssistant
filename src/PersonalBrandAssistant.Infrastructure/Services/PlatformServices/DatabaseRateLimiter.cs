using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices;

public sealed class DatabaseRateLimiter : IRateLimiter
{
    private const int MaxConcurrencyRetries = 3;
    private static readonly TimeSpan InstagramCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<DatabaseRateLimiter> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IMemoryCache _cache;

    public DatabaseRateLimiter(
        IApplicationDbContext dbContext,
        ILogger<DatabaseRateLimiter> logger,
        TimeProvider timeProvider,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _logger = logger;
        _timeProvider = timeProvider;
        _cache = cache;
    }

    public async Task<Result<RateLimitDecision>> CanMakeRequestAsync(
        PlatformType platform,
        string endpoint,
        CancellationToken ct)
    {
        // Instagram: check cache first
        if (platform == PlatformType.Instagram)
        {
            var cacheKey = CacheKey(platform, endpoint);
            if (_cache.TryGetValue(cacheKey, out RateLimitDecision? cached))
            {
                return Result.Success(cached!);
            }

            var result = await EvaluateRateLimitAsync(platform, endpoint, ct);
            if (result.IsSuccess)
            {
                _cache.Set(cacheKey, result.Value, InstagramCacheTtl);
            }

            return result;
        }

        return await EvaluateRateLimitAsync(platform, endpoint, ct);
    }

    public async Task<Result<bool>> RecordRequestAsync(
        PlatformType platform,
        string endpoint,
        int remaining,
        DateTimeOffset? resetAt,
        CancellationToken ct)
    {
        var entity = await _dbContext.Platforms
            .FirstOrDefaultAsync(p => p.Type == platform, ct);

        if (entity is null)
        {
            return Result.NotFound<bool>($"Platform '{platform}' not found");
        }

        entity.RateLimitState.Endpoints[endpoint] = new EndpointRateLimit
        {
            RemainingCalls = remaining,
            ResetAt = resetAt,
        };

        if (platform == PlatformType.YouTube)
        {
            entity.RateLimitState.DailyQuotaUsed =
                (entity.RateLimitState.DailyQuotaLimit ?? 10000) - remaining;
        }

        // Invalidate Instagram cache after recording new state
        if (platform == PlatformType.Instagram)
        {
            _cache.Remove(CacheKey(platform, endpoint));
        }

        _logger.LogDebug(
            "Recorded rate limit for {Platform}/{Endpoint}: remaining={Remaining}, resetAt={ResetAt}",
            platform, endpoint, remaining, resetAt);

        await _dbContext.SaveChangesAsync(ct);
        return Result.Success(true);
    }

    public async Task<Result<RateLimitStatus>> GetStatusAsync(PlatformType platform, CancellationToken ct)
    {
        var entity = await _dbContext.Platforms
            .FirstOrDefaultAsync(p => p.Type == platform, ct);

        if (entity is null)
        {
            return Result.NotFound<RateLimitStatus>($"Platform '{platform}' not found");
        }

        var state = entity.RateLimitState;
        var now = _timeProvider.GetUtcNow();

        // YouTube daily quota check first (short-circuit)
        if (platform == PlatformType.YouTube &&
            state.DailyQuotaUsed >= state.DailyQuotaLimit &&
            state.DailyQuotaLimit > 0 &&
            state.QuotaResetAt > now)
        {
            return Result.Success(new RateLimitStatus(
                RemainingCalls: 0,
                ResetAt: state.QuotaResetAt,
                IsLimited: true));
        }

        if (state.Endpoints.Count == 0)
        {
            return Result.Success(new RateLimitStatus(RemainingCalls: null, ResetAt: null, IsLimited: false));
        }

        var activeEndpoints = state.Endpoints
            .Where(e => e.Value.ResetAt == null || e.Value.ResetAt > now)
            .ToList();

        if (activeEndpoints.Count == 0)
        {
            return Result.Success(new RateLimitStatus(RemainingCalls: null, ResetAt: null, IsLimited: false));
        }

        var minRemaining = activeEndpoints.Min(e => e.Value.RemainingCalls ?? int.MaxValue);
        var resetTimes = activeEndpoints
            .Where(e => e.Value.ResetAt != null)
            .Select(e => e.Value.ResetAt!.Value)
            .ToList();
        var nearestReset = resetTimes.Count > 0 ? resetTimes.Min() : (DateTimeOffset?)null;
        var isLimited = activeEndpoints.Any(e => e.Value.RemainingCalls == 0 && e.Value.ResetAt > now);

        return Result.Success(new RateLimitStatus(
            RemainingCalls: minRemaining == int.MaxValue ? null : minRemaining,
            ResetAt: nearestReset,
            IsLimited: isLimited));
    }

    private async Task<Result<RateLimitDecision>> EvaluateRateLimitAsync(
        PlatformType platform,
        string endpoint,
        CancellationToken ct)
    {
        var entity = await _dbContext.Platforms
            .FirstOrDefaultAsync(p => p.Type == platform, ct);

        if (entity is null)
        {
            return Result.NotFound<RateLimitDecision>($"Platform '{platform}' not found");
        }

        var state = entity.RateLimitState;
        var now = _timeProvider.GetUtcNow();

        // YouTube daily quota check with concurrency-safe reset
        if (platform == PlatformType.YouTube)
        {
            if (state.QuotaResetAt != null && state.QuotaResetAt <= now)
            {
                await ResetYouTubeQuotaWithRetryAsync(entity, now, ct);
            }
            else if (state.DailyQuotaUsed >= state.DailyQuotaLimit && state.DailyQuotaLimit > 0)
            {
                return Result.Success(new RateLimitDecision(
                    Allowed: false,
                    RetryAt: state.QuotaResetAt,
                    Reason: $"YouTube daily quota exceeded ({state.DailyQuotaUsed}/{state.DailyQuotaLimit})"));
            }
        }

        // Per-endpoint check
        if (state.Endpoints.TryGetValue(endpoint, out var endpointLimit))
        {
            if (endpointLimit.RemainingCalls == 0)
            {
                if (endpointLimit.ResetAt != null && endpointLimit.ResetAt > now)
                {
                    return Result.Success(new RateLimitDecision(
                        Allowed: false,
                        RetryAt: endpointLimit.ResetAt,
                        Reason: $"Rate limit exhausted for {platform}/{endpoint}, resets at {endpointLimit.ResetAt}"));
                }

                // ResetAt is in the past or null — treat as reset
            }
        }

        return Result.Success(new RateLimitDecision(Allowed: true, RetryAt: null, Reason: null));
    }

    private async Task ResetYouTubeQuotaWithRetryAsync(
        Domain.Entities.Platform entity,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            try
            {
                entity.RateLimitState.DailyQuotaUsed = 0;
                entity.RateLimitState.QuotaResetAt = CalculateNextMidnightPacific(now);
                await _dbContext.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries - 1)
            {
                _logger.LogWarning(
                    "Concurrency conflict resetting YouTube quota (attempt {Attempt}/{Max}), retrying",
                    attempt + 1, MaxConcurrencyRetries);

                // Reload entity to get fresh xmin token
                var entry = _dbContext as DbContext;
                if (entry != null)
                {
                    await entry.Entry(entity).ReloadAsync(ct);
                }

                // If another request already reset the quota, no need to retry
                if (entity.RateLimitState.QuotaResetAt > now)
                {
                    return;
                }
            }
        }
    }

    // Midnight Pacific is unambiguous during DST transitions (no 2am ambiguity applies)
    private static DateTimeOffset CalculateNextMidnightPacific(DateTimeOffset now)
    {
        var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var pacificNow = TimeZoneInfo.ConvertTime(now, pacificZone);
        var nextMidnight = pacificNow.Date.AddDays(1);
        var nextMidnightDto = new DateTimeOffset(nextMidnight, pacificZone.GetUtcOffset(nextMidnight));
        return nextMidnightDto.ToUniversalTime();
    }

    private static string CacheKey(PlatformType platform, string endpoint) =>
        $"rate-limit:{platform}:{endpoint}";
}
