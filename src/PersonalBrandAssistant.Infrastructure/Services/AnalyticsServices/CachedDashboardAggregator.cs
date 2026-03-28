using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

internal sealed class CachedDashboardAggregator(
    IDashboardAggregator inner,
    HybridCache cache,
    DashboardRefreshLimiter refreshLimiter,
    ILogger<CachedDashboardAggregator> logger,
    TimeProvider timeProvider) : IDashboardAggregator, IDashboardCacheInvalidator
{
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(1);

    private static readonly HybridCacheEntryOptions SummaryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    private static readonly HybridCacheEntryOptions TimelineOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(15),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    private static readonly HybridCacheEntryOptions PlatformSummaryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(15),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task<Result<DashboardSummary>> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var key = $"dashboard:summary:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
        try
        {
            var value = await cache.GetOrCreateAsync<DashboardSummary>(
                key,
                async token =>
                {
                    var result = await inner.GetSummaryAsync(from, to, token);
                    return result.IsSuccess
                        ? result.Value!
                        : throw new FactoryFailureException(result.ErrorCode, result.Errors);
                },
                SummaryOptions,
                tags: ["dashboard"], // Summary aggregates all sources, not just social
                cancellationToken: ct);
            return Result.Success(value);
        }
        catch (FactoryFailureException ex)
        {
            return Result.Failure<DashboardSummary>(ex.Code, [.. ex.Messages]);
        }
    }

    public async Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var key = $"dashboard:timeline:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
        try
        {
            // Cache as List<> (serializable) and return as IReadOnlyList<>
            var value = await cache.GetOrCreateAsync<List<DailyEngagement>>(
                key,
                async token =>
                {
                    var result = await inner.GetTimelineAsync(from, to, token);
                    return result.IsSuccess
                        ? result.Value!.ToList()
                        : throw new FactoryFailureException(result.ErrorCode, result.Errors);
                },
                TimelineOptions,
                tags: ["dashboard", "social"],
                cancellationToken: ct);
            return Result.Success<IReadOnlyList<DailyEngagement>>(value);
        }
        catch (FactoryFailureException ex)
        {
            return Result.Failure<IReadOnlyList<DailyEngagement>>(ex.Code, [.. ex.Messages]);
        }
    }

    public async Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var key = $"dashboard:platforms:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
        try
        {
            var value = await cache.GetOrCreateAsync<List<PlatformSummary>>(
                key,
                async token =>
                {
                    var result = await inner.GetPlatformSummariesAsync(from, to, token);
                    return result.IsSuccess
                        ? result.Value!.ToList()
                        : throw new FactoryFailureException(result.ErrorCode, result.Errors);
                },
                PlatformSummaryOptions,
                tags: ["dashboard", "social"],
                cancellationToken: ct);
            return Result.Success<IReadOnlyList<PlatformSummary>>(value);
        }
        catch (FactoryFailureException ex)
        {
            return Result.Failure<IReadOnlyList<PlatformSummary>>(ex.Code, [.. ex.Messages]);
        }
    }

    public async Task<bool> TryInvalidateAsync(CancellationToken ct)
    {
        if (!refreshLimiter.TryAcquire(timeProvider, RefreshCooldown))
        {
            logger.LogWarning("Dashboard cache refresh rate limited");
            return false;
        }

        await cache.RemoveByTagAsync("dashboard", ct);
        logger.LogInformation("Dashboard cache invalidated");
        return true;
    }

    /// <summary>Thrown inside cache factory to prevent caching failures. Never escapes the class.</summary>
    private sealed class FactoryFailureException(ErrorCode code, IReadOnlyList<string> messages) : Exception
    {
        public ErrorCode Code => code;
        public IReadOnlyList<string> Messages => messages;
    }
}
