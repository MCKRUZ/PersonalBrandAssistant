diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/DatabaseRateLimiter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/DatabaseRateLimiter.cs
new file mode 100644
index 0000000..b98aff2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/DatabaseRateLimiter.cs
@@ -0,0 +1,188 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Caching.Memory;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+
+public sealed class DatabaseRateLimiter : IRateLimiter
+{
+    private static readonly TimeSpan InstagramCacheTtl = TimeSpan.FromMinutes(5);
+
+    private readonly IApplicationDbContext _dbContext;
+    private readonly ILogger<DatabaseRateLimiter> _logger;
+    private readonly TimeProvider _timeProvider;
+    private readonly IMemoryCache _cache;
+
+    public DatabaseRateLimiter(
+        IApplicationDbContext dbContext,
+        ILogger<DatabaseRateLimiter> logger,
+        TimeProvider timeProvider,
+        IMemoryCache cache)
+    {
+        _dbContext = dbContext;
+        _logger = logger;
+        _timeProvider = timeProvider;
+        _cache = cache;
+    }
+
+    public async Task<RateLimitDecision> CanMakeRequestAsync(
+        PlatformType platform,
+        string endpoint,
+        CancellationToken ct)
+    {
+        // Instagram: check cache first
+        if (platform == PlatformType.Instagram)
+        {
+            var cacheKey = $"rate-limit:{platform}:{endpoint}";
+            if (_cache.TryGetValue(cacheKey, out RateLimitDecision? cached))
+            {
+                return cached!;
+            }
+
+            var decision = await EvaluateRateLimitAsync(platform, endpoint, ct);
+            _cache.Set(cacheKey, decision, InstagramCacheTtl);
+            return decision;
+        }
+
+        return await EvaluateRateLimitAsync(platform, endpoint, ct);
+    }
+
+    public async Task RecordRequestAsync(
+        PlatformType platform,
+        string endpoint,
+        int remaining,
+        DateTimeOffset? resetAt,
+        CancellationToken ct)
+    {
+        var entity = await _dbContext.Platforms
+            .FirstAsync(p => p.Type == platform, ct);
+
+        entity.RateLimitState.Endpoints[endpoint] = new EndpointRateLimit
+        {
+            RemainingCalls = remaining,
+            ResetAt = resetAt,
+        };
+
+        if (platform == PlatformType.YouTube)
+        {
+            entity.RateLimitState.DailyQuotaUsed =
+                (entity.RateLimitState.DailyQuotaLimit ?? 10000) - remaining;
+        }
+
+        _logger.LogDebug(
+            "Recorded rate limit for {Platform}/{Endpoint}: remaining={Remaining}, resetAt={ResetAt}",
+            platform, endpoint, remaining, resetAt);
+
+        await _dbContext.SaveChangesAsync(ct);
+    }
+
+    public async Task<RateLimitStatus> GetStatusAsync(PlatformType platform, CancellationToken ct)
+    {
+        var entity = await _dbContext.Platforms
+            .FirstAsync(p => p.Type == platform, ct);
+
+        var state = entity.RateLimitState;
+        var now = _timeProvider.GetUtcNow();
+
+        if (state.Endpoints.Count == 0)
+        {
+            return new RateLimitStatus(RemainingCalls: null, ResetAt: null, IsLimited: false);
+        }
+
+        var activeEndpoints = state.Endpoints
+            .Where(e => e.Value.ResetAt == null || e.Value.ResetAt > now)
+            .ToList();
+
+        if (activeEndpoints.Count == 0)
+        {
+            return new RateLimitStatus(RemainingCalls: null, ResetAt: null, IsLimited: false);
+        }
+
+        var minRemaining = activeEndpoints.Min(e => e.Value.RemainingCalls ?? int.MaxValue);
+        var nearestReset = activeEndpoints
+            .Where(e => e.Value.ResetAt != null)
+            .Select(e => e.Value.ResetAt!.Value)
+            .DefaultIfEmpty()
+            .Min();
+        var isLimited = activeEndpoints.Any(e => e.Value.RemainingCalls == 0 && e.Value.ResetAt > now);
+
+        // YouTube daily quota check
+        if (platform == PlatformType.YouTube &&
+            state.DailyQuotaUsed >= state.DailyQuotaLimit &&
+            state.QuotaResetAt > now)
+        {
+            return new RateLimitStatus(
+                RemainingCalls: 0,
+                ResetAt: state.QuotaResetAt,
+                IsLimited: true);
+        }
+
+        return new RateLimitStatus(
+            RemainingCalls: minRemaining == int.MaxValue ? null : minRemaining,
+            ResetAt: nearestReset == default ? null : nearestReset,
+            IsLimited: isLimited);
+    }
+
+    private async Task<RateLimitDecision> EvaluateRateLimitAsync(
+        PlatformType platform,
+        string endpoint,
+        CancellationToken ct)
+    {
+        var entity = await _dbContext.Platforms
+            .FirstAsync(p => p.Type == platform, ct);
+
+        var state = entity.RateLimitState;
+        var now = _timeProvider.GetUtcNow();
+
+        // YouTube daily quota check
+        if (platform == PlatformType.YouTube)
+        {
+            if (state.QuotaResetAt != null && state.QuotaResetAt <= now)
+            {
+                // Reset expired quota
+                state.DailyQuotaUsed = 0;
+                state.QuotaResetAt = CalculateNextMidnightPacific(now);
+                await _dbContext.SaveChangesAsync(ct);
+            }
+            else if (state.DailyQuotaUsed >= state.DailyQuotaLimit && state.DailyQuotaLimit > 0)
+            {
+                return new RateLimitDecision(
+                    Allowed: false,
+                    RetryAt: state.QuotaResetAt,
+                    Reason: $"YouTube daily quota exceeded ({state.DailyQuotaUsed}/{state.DailyQuotaLimit})");
+            }
+        }
+
+        // Per-endpoint check
+        if (state.Endpoints.TryGetValue(endpoint, out var endpointLimit))
+        {
+            if (endpointLimit.RemainingCalls == 0)
+            {
+                if (endpointLimit.ResetAt != null && endpointLimit.ResetAt > now)
+                {
+                    return new RateLimitDecision(
+                        Allowed: false,
+                        RetryAt: endpointLimit.ResetAt,
+                        Reason: $"Rate limit exhausted for {platform}/{endpoint}, resets at {endpointLimit.ResetAt}");
+                }
+
+                // ResetAt is in the past, treat as reset
+            }
+        }
+
+        return new RateLimitDecision(Allowed: true, RetryAt: null, Reason: null);
+    }
+
+    private static DateTimeOffset CalculateNextMidnightPacific(DateTimeOffset now)
+    {
+        var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
+        var pacificNow = TimeZoneInfo.ConvertTime(now, pacificZone);
+        var nextMidnight = pacificNow.Date.AddDays(1);
+        var nextMidnightDto = new DateTimeOffset(nextMidnight, pacificZone.GetUtcOffset(nextMidnight));
+        return nextMidnightDto.ToUniversalTime();
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/DatabaseRateLimiterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/DatabaseRateLimiterTests.cs
new file mode 100644
index 0000000..9c6a768
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/DatabaseRateLimiterTests.cs
@@ -0,0 +1,252 @@
+using Microsoft.Extensions.Caching.Memory;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class DatabaseRateLimiterTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext;
+    private readonly IMemoryCache _cache;
+    private readonly FakeTimeProvider _timeProvider;
+    private readonly DatabaseRateLimiter _sut;
+
+    public DatabaseRateLimiterTests()
+    {
+        _dbContext = new Mock<IApplicationDbContext>();
+        _cache = new MemoryCache(new MemoryCacheOptions());
+        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
+        _sut = new DatabaseRateLimiter(
+            _dbContext.Object,
+            NullLogger<DatabaseRateLimiter>.Instance,
+            _timeProvider,
+            _cache);
+    }
+
+    private void SetupPlatforms(params Domain.Entities.Platform[] platforms)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+    }
+
+    private static Domain.Entities.Platform CreatePlatform(
+        PlatformType type,
+        PlatformRateLimitState? rateLimitState = null)
+    {
+        return new Domain.Entities.Platform
+        {
+            Type = type,
+            DisplayName = type.ToString(),
+            IsConnected = true,
+            RateLimitState = rateLimitState ?? new PlatformRateLimitState(),
+        };
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_ReturnsAllowed_WhenNoRateLimitState()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX);
+        SetupPlatforms(platform);
+
+        var decision = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);
+
+        Assert.True(decision.Allowed);
+        Assert.Null(decision.RetryAt);
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_ReturnsAllowed_WhenRemainingGreaterThanZero()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
+        {
+            Endpoints = new Dictionary<string, EndpointRateLimit>
+            {
+                ["tweets"] = new() { RemainingCalls = 5, ResetAt = _timeProvider.GetUtcNow().AddMinutes(15) },
+            },
+        });
+        SetupPlatforms(platform);
+
+        var decision = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);
+
+        Assert.True(decision.Allowed);
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_ReturnsDenied_WhenRemainingIsZero()
+    {
+        var resetAt = _timeProvider.GetUtcNow().AddMinutes(15);
+        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
+        {
+            Endpoints = new Dictionary<string, EndpointRateLimit>
+            {
+                ["tweets"] = new() { RemainingCalls = 0, ResetAt = resetAt },
+            },
+        });
+        SetupPlatforms(platform);
+
+        var decision = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);
+
+        Assert.False(decision.Allowed);
+        Assert.Equal(resetAt, decision.RetryAt);
+        Assert.NotNull(decision.Reason);
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_ReturnsDenied_WhenYouTubeDailyQuotaExceeded()
+    {
+        var quotaResetAt = _timeProvider.GetUtcNow().AddHours(6);
+        var platform = CreatePlatform(PlatformType.YouTube, new PlatformRateLimitState
+        {
+            DailyQuotaUsed = 10000,
+            DailyQuotaLimit = 10000,
+            QuotaResetAt = quotaResetAt,
+        });
+        SetupPlatforms(platform);
+
+        var decision = await _sut.CanMakeRequestAsync(PlatformType.YouTube, "videos.insert", CancellationToken.None);
+
+        Assert.False(decision.Allowed);
+        Assert.Equal(quotaResetAt, decision.RetryAt);
+        Assert.Contains("daily quota", decision.Reason, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task RecordRequestAsync_UpdatesPlatformRateLimitState()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX);
+        SetupPlatforms(platform);
+        var resetAt = _timeProvider.GetUtcNow().AddMinutes(15);
+
+        await _sut.RecordRequestAsync(PlatformType.TwitterX, "tweets", 99, resetAt, CancellationToken.None);
+
+        Assert.Equal(99, platform.RateLimitState.Endpoints["tweets"].RemainingCalls);
+        Assert.Equal(resetAt, platform.RateLimitState.Endpoints["tweets"].ResetAt);
+        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task RecordRequestAsync_CreatesEndpointEntry_WhenNotExists()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX);
+        SetupPlatforms(platform);
+        var resetAt = _timeProvider.GetUtcNow().AddMinutes(15);
+
+        await _sut.RecordRequestAsync(PlatformType.TwitterX, "tweets", 50, resetAt, CancellationToken.None);
+
+        Assert.True(platform.RateLimitState.Endpoints.ContainsKey("tweets"));
+        Assert.Equal(50, platform.RateLimitState.Endpoints["tweets"].RemainingCalls);
+    }
+
+    [Fact]
+    public async Task RecordRequestAsync_UpdatesExistingEndpointEntry()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
+        {
+            Endpoints = new Dictionary<string, EndpointRateLimit>
+            {
+                ["tweets"] = new() { RemainingCalls = 50, ResetAt = _timeProvider.GetUtcNow().AddMinutes(10) },
+            },
+        });
+        SetupPlatforms(platform);
+        var newResetAt = _timeProvider.GetUtcNow().AddMinutes(15);
+
+        await _sut.RecordRequestAsync(PlatformType.TwitterX, "tweets", 49, newResetAt, CancellationToken.None);
+
+        Assert.Equal(49, platform.RateLimitState.Endpoints["tweets"].RemainingCalls);
+        Assert.Equal(newResetAt, platform.RateLimitState.Endpoints["tweets"].ResetAt);
+    }
+
+    [Fact]
+    public async Task GetStatusAsync_ReturnsAggregateStatusAcrossEndpoints()
+    {
+        var nearestReset = _timeProvider.GetUtcNow().AddMinutes(5);
+        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
+        {
+            Endpoints = new Dictionary<string, EndpointRateLimit>
+            {
+                ["tweets"] = new() { RemainingCalls = 10, ResetAt = _timeProvider.GetUtcNow().AddMinutes(10) },
+                ["users"] = new() { RemainingCalls = 0, ResetAt = nearestReset },
+            },
+        });
+        SetupPlatforms(platform);
+
+        var status = await _sut.GetStatusAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.True(status.IsLimited);
+        Assert.Equal(0, status.RemainingCalls);
+        Assert.Equal(nearestReset, status.ResetAt);
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_ResetsYouTubeQuota_WhenQuotaResetInPast()
+    {
+        var platform = CreatePlatform(PlatformType.YouTube, new PlatformRateLimitState
+        {
+            DailyQuotaUsed = 5000,
+            DailyQuotaLimit = 10000,
+            QuotaResetAt = _timeProvider.GetUtcNow().AddHours(-1),
+        });
+        SetupPlatforms(platform);
+
+        var decision = await _sut.CanMakeRequestAsync(PlatformType.YouTube, "videos.list", CancellationToken.None);
+
+        Assert.True(decision.Allowed);
+        Assert.Equal(0, platform.RateLimitState.DailyQuotaUsed);
+        Assert.True(platform.RateLimitState.QuotaResetAt > _timeProvider.GetUtcNow());
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_Instagram_CachesResult()
+    {
+        var platform = CreatePlatform(PlatformType.Instagram, new PlatformRateLimitState
+        {
+            Endpoints = new Dictionary<string, EndpointRateLimit>
+            {
+                ["publish"] = new() { RemainingCalls = 10, ResetAt = _timeProvider.GetUtcNow().AddHours(1) },
+            },
+        });
+        SetupPlatforms(platform);
+
+        var decision1 = await _sut.CanMakeRequestAsync(PlatformType.Instagram, "publish", CancellationToken.None);
+        var decision2 = await _sut.CanMakeRequestAsync(PlatformType.Instagram, "publish", CancellationToken.None);
+
+        Assert.True(decision1.Allowed);
+        Assert.True(decision2.Allowed);
+        // Second call uses cache - Platforms accessed only once for the first call
+        _dbContext.Verify(db => db.Platforms, Times.Once);
+    }
+
+    [Fact]
+    public async Task CanMakeRequestAsync_ReturnsAllowed_WhenResetAtInPast()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
+        {
+            Endpoints = new Dictionary<string, EndpointRateLimit>
+            {
+                ["tweets"] = new() { RemainingCalls = 0, ResetAt = _timeProvider.GetUtcNow().AddMinutes(-5) },
+            },
+        });
+        SetupPlatforms(platform);
+
+        var decision = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);
+
+        Assert.True(decision.Allowed);
+    }
+}
+
+internal class FakeTimeProvider : TimeProvider
+{
+    private DateTimeOffset _utcNow;
+
+    public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
+
+    public override DateTimeOffset GetUtcNow() => _utcNow;
+
+    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
+}
