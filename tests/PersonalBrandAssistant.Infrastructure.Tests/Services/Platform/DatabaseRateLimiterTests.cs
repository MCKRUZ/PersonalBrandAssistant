using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class DatabaseRateLimiterTests
{
    private readonly Mock<IApplicationDbContext> _dbContext;
    private readonly IMemoryCache _cache;
    private readonly FakeTimeProvider _timeProvider;
    private readonly DatabaseRateLimiter _sut;

    public DatabaseRateLimiterTests()
    {
        _dbContext = new Mock<IApplicationDbContext>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        _sut = new DatabaseRateLimiter(
            _dbContext.Object,
            NullLogger<DatabaseRateLimiter>.Instance,
            _timeProvider,
            _cache);
    }

    private void SetupPlatforms(params Domain.Entities.Platform[] platforms)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
    }

    private static Domain.Entities.Platform CreatePlatform(
        PlatformType type,
        PlatformRateLimitState? rateLimitState = null)
    {
        return new Domain.Entities.Platform
        {
            Type = type,
            DisplayName = type.ToString(),
            IsConnected = true,
            RateLimitState = rateLimitState ?? new PlatformRateLimitState(),
        };
    }

    [Fact]
    public async Task CanMakeRequestAsync_ReturnsAllowed_WhenNoRateLimitState()
    {
        var platform = CreatePlatform(PlatformType.TwitterX);
        SetupPlatforms(platform);

        var result = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Allowed);
        Assert.Null(result.Value.RetryAt);
    }

    [Fact]
    public async Task CanMakeRequestAsync_ReturnsAllowed_WhenRemainingGreaterThanZero()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["tweets"] = new() { RemainingCalls = 5, ResetAt = _timeProvider.GetUtcNow().AddMinutes(15) },
            },
        });
        SetupPlatforms(platform);

        var result = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Allowed);
    }

    [Fact]
    public async Task CanMakeRequestAsync_ReturnsDenied_WhenRemainingIsZero()
    {
        var resetAt = _timeProvider.GetUtcNow().AddMinutes(15);
        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["tweets"] = new() { RemainingCalls = 0, ResetAt = resetAt },
            },
        });
        SetupPlatforms(platform);

        var result = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Allowed);
        Assert.Equal(resetAt, result.Value.RetryAt);
        Assert.NotNull(result.Value.Reason);
    }

    [Fact]
    public async Task CanMakeRequestAsync_ReturnsDenied_WhenYouTubeDailyQuotaExceeded()
    {
        var quotaResetAt = _timeProvider.GetUtcNow().AddHours(6);
        var platform = CreatePlatform(PlatformType.YouTube, new PlatformRateLimitState
        {
            DailyQuotaUsed = 10000,
            DailyQuotaLimit = 10000,
            QuotaResetAt = quotaResetAt,
        });
        SetupPlatforms(platform);

        var result = await _sut.CanMakeRequestAsync(PlatformType.YouTube, "videos.insert", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Allowed);
        Assert.Equal(quotaResetAt, result.Value.RetryAt);
        Assert.Contains("daily quota", result.Value.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordRequestAsync_UpdatesPlatformRateLimitState()
    {
        var platform = CreatePlatform(PlatformType.TwitterX);
        SetupPlatforms(platform);
        var resetAt = _timeProvider.GetUtcNow().AddMinutes(15);

        var result = await _sut.RecordRequestAsync(PlatformType.TwitterX, "tweets", 99, resetAt, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99, platform.RateLimitState.Endpoints["tweets"].RemainingCalls);
        Assert.Equal(resetAt, platform.RateLimitState.Endpoints["tweets"].ResetAt);
        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRequestAsync_CreatesEndpointEntry_WhenNotExists()
    {
        var platform = CreatePlatform(PlatformType.TwitterX);
        SetupPlatforms(platform);
        var resetAt = _timeProvider.GetUtcNow().AddMinutes(15);

        await _sut.RecordRequestAsync(PlatformType.TwitterX, "tweets", 50, resetAt, CancellationToken.None);

        Assert.True(platform.RateLimitState.Endpoints.ContainsKey("tweets"));
        Assert.Equal(50, platform.RateLimitState.Endpoints["tweets"].RemainingCalls);
    }

    [Fact]
    public async Task RecordRequestAsync_UpdatesExistingEndpointEntry()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["tweets"] = new() { RemainingCalls = 50, ResetAt = _timeProvider.GetUtcNow().AddMinutes(10) },
            },
        });
        SetupPlatforms(platform);
        var newResetAt = _timeProvider.GetUtcNow().AddMinutes(15);

        await _sut.RecordRequestAsync(PlatformType.TwitterX, "tweets", 49, newResetAt, CancellationToken.None);

        Assert.Equal(49, platform.RateLimitState.Endpoints["tweets"].RemainingCalls);
        Assert.Equal(newResetAt, platform.RateLimitState.Endpoints["tweets"].ResetAt);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsAggregateStatusAcrossEndpoints()
    {
        var nearestReset = _timeProvider.GetUtcNow().AddMinutes(5);
        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["tweets"] = new() { RemainingCalls = 10, ResetAt = _timeProvider.GetUtcNow().AddMinutes(10) },
                ["users"] = new() { RemainingCalls = 0, ResetAt = nearestReset },
            },
        });
        SetupPlatforms(platform);

        var result = await _sut.GetStatusAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsLimited);
        Assert.Equal(0, result.Value.RemainingCalls);
        Assert.Equal(nearestReset, result.Value.ResetAt);
    }

    [Fact]
    public async Task CanMakeRequestAsync_ResetsYouTubeQuota_WhenQuotaResetInPast()
    {
        var platform = CreatePlatform(PlatformType.YouTube, new PlatformRateLimitState
        {
            DailyQuotaUsed = 5000,
            DailyQuotaLimit = 10000,
            QuotaResetAt = _timeProvider.GetUtcNow().AddHours(-1),
        });
        SetupPlatforms(platform);

        var result = await _sut.CanMakeRequestAsync(PlatformType.YouTube, "videos.list", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Allowed);
        Assert.Equal(0, platform.RateLimitState.DailyQuotaUsed);
        Assert.True(platform.RateLimitState.QuotaResetAt > _timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task CanMakeRequestAsync_Instagram_CachesResult()
    {
        var platform = CreatePlatform(PlatformType.Instagram, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["publish"] = new() { RemainingCalls = 10, ResetAt = _timeProvider.GetUtcNow().AddHours(1) },
            },
        });
        SetupPlatforms(platform);

        var result1 = await _sut.CanMakeRequestAsync(PlatformType.Instagram, "publish", CancellationToken.None);
        var result2 = await _sut.CanMakeRequestAsync(PlatformType.Instagram, "publish", CancellationToken.None);

        Assert.True(result1.Value!.Allowed);
        Assert.True(result2.Value!.Allowed);
        // Second call uses cache - Platforms accessed only once for the first call
        _dbContext.Verify(db => db.Platforms, Times.Once);
    }

    [Fact]
    public async Task CanMakeRequestAsync_ReturnsAllowed_WhenResetAtInPast()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["tweets"] = new() { RemainingCalls = 0, ResetAt = _timeProvider.GetUtcNow().AddMinutes(-5) },
            },
        });
        SetupPlatforms(platform);

        var result = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Allowed);
    }

    [Fact]
    public async Task CanMakeRequestAsync_ReturnsNotFound_WhenPlatformMissing()
    {
        SetupPlatforms(); // empty

        var result = await _sut.CanMakeRequestAsync(PlatformType.TwitterX, "tweets", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNotLimited_WhenNoEndpoints()
    {
        var platform = CreatePlatform(PlatformType.TwitterX);
        SetupPlatforms(platform);

        var result = await _sut.GetStatusAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsLimited);
        Assert.Null(result.Value.RemainingCalls);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsLimited_WhenYouTubeDailyQuotaExceeded()
    {
        var quotaReset = _timeProvider.GetUtcNow().AddHours(6);
        var platform = CreatePlatform(PlatformType.YouTube, new PlatformRateLimitState
        {
            DailyQuotaUsed = 10000,
            DailyQuotaLimit = 10000,
            QuotaResetAt = quotaReset,
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["videos.list"] = new() { RemainingCalls = 50, ResetAt = _timeProvider.GetUtcNow().AddHours(1) },
            },
        });
        SetupPlatforms(platform);

        var result = await _sut.GetStatusAsync(PlatformType.YouTube, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsLimited);
        Assert.Equal(0, result.Value.RemainingCalls);
        Assert.Equal(quotaReset, result.Value.ResetAt);
    }

    [Fact]
    public async Task RecordRequestAsync_SetsYouTubeDailyQuotaUsed()
    {
        var platform = CreatePlatform(PlatformType.YouTube, new PlatformRateLimitState
        {
            DailyQuotaLimit = 10000,
        });
        SetupPlatforms(platform);

        await _sut.RecordRequestAsync(PlatformType.YouTube, "videos.insert", 8400, null, CancellationToken.None);

        Assert.Equal(1600, platform.RateLimitState.DailyQuotaUsed);
    }

    [Fact]
    public async Task RecordRequestAsync_InvalidatesCacheForInstagram()
    {
        var platform = CreatePlatform(PlatformType.Instagram, new PlatformRateLimitState
        {
            Endpoints = new Dictionary<string, EndpointRateLimit>
            {
                ["publish"] = new() { RemainingCalls = 10, ResetAt = _timeProvider.GetUtcNow().AddHours(1) },
            },
        });
        SetupPlatforms(platform);

        // Populate cache
        await _sut.CanMakeRequestAsync(PlatformType.Instagram, "publish", CancellationToken.None);

        // Record should invalidate cache
        await _sut.RecordRequestAsync(PlatformType.Instagram, "publish", 0, _timeProvider.GetUtcNow().AddHours(1), CancellationToken.None);

        // Next call should hit DB again (Platforms accessed: once for first CanMake, once for Record, once for this CanMake)
        SetupPlatforms(platform); // re-setup since mock DbSet is consumed
        var result = await _sut.CanMakeRequestAsync(PlatformType.Instagram, "publish", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Allowed);
    }
}

internal class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
