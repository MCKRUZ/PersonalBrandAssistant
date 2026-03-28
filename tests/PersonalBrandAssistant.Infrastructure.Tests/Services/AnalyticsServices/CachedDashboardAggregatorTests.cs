using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.AnalyticsServices;

public class CachedDashboardAggregatorTests : IDisposable
{
    private readonly Mock<IDashboardAggregator> _innerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly ServiceProvider _sp;
    private readonly CachedDashboardAggregator _sut;

    public CachedDashboardAggregatorTests()
    {
        _timeProviderMock
            .Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddHybridCache();
        services.AddLogging();
        _sp = services.BuildServiceProvider();

        _sut = new CachedDashboardAggregator(
            _innerMock.Object,
            _sp.GetRequiredService<HybridCache>(),
            new DashboardRefreshLimiter(),
            NullLogger<CachedDashboardAggregator>.Instance,
            _timeProviderMock.Object);
    }

    public void Dispose() => _sp.Dispose();

    [Fact]
    public async Task GetSummaryAsync_SecondCallReturnsCachedResult()
    {
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
        var summary = CreateTestSummary();

        _innerMock
            .Setup(x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(summary));

        await _sut.GetSummaryAsync(from, to, CancellationToken.None);
        var result = await _sut.GetSummaryAsync(from, to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(summary.TotalEngagement, result.Value!.TotalEngagement);
        _innerMock.Verify(
            x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task GetSummaryAsync_RefreshBypassesCache()
    {
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
        var summary1 = CreateTestSummary(totalEngagement: 100);
        var summary2 = CreateTestSummary(totalEngagement: 200);

        _innerMock
            .SetupSequence(x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(summary1))
            .ReturnsAsync(Result.Success(summary2));

        // First call: cache miss, populates cache
        var result1 = await _sut.GetSummaryAsync(from, to, CancellationToken.None);

        // Invalidate cache
        var invalidated = await _sut.TryInvalidateAsync(CancellationToken.None);

        // Second call: cache was invalidated, calls inner again
        var result2 = await _sut.GetSummaryAsync(from, to, CancellationToken.None);

        Assert.True(invalidated);
        Assert.Equal(100, result1.Value!.TotalEngagement);
        Assert.Equal(200, result2.Value!.TotalEngagement);
        _innerMock.Verify(
            x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TryInvalidateAsync_RejectsSecondRefreshWithinCooldown()
    {
        var now = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now);

        var first = await _sut.TryInvalidateAsync(CancellationToken.None);

        // Move time forward only 30 seconds (within 1-minute cooldown)
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now.AddSeconds(30));

        var second = await _sut.TryInvalidateAsync(CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task TryInvalidateAsync_AllowsRefreshAfterCooldown()
    {
        var now = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now);

        var first = await _sut.TryInvalidateAsync(CancellationToken.None);

        // Move time forward 61 seconds (past cooldown)
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now.AddSeconds(61));

        var second = await _sut.TryInvalidateAsync(CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public async Task GetTimelineAsync_CachesResult()
    {
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);
        var timeline = new List<DailyEngagement>
        {
            new(DateOnly.FromDateTime(from.UtcDateTime),
                [new PlatformDailyMetrics(PlatformType.TwitterX, 5, 2, 1, 8)],
                8)
        };

        _innerMock
            .Setup(x => x.GetTimelineAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<DailyEngagement>>(timeline));

        await _sut.GetTimelineAsync(from, to, CancellationToken.None);
        await _sut.GetTimelineAsync(from, to, CancellationToken.None);

        _innerMock.Verify(
            x => x.GetTimelineAsync(from, to, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_CachesResult()
    {
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
        var summaries = new List<PlatformSummary>
        {
            new(PlatformType.TwitterX, 1000, 5, 50.0, "Top Post", "https://x.com/1", true)
        };

        _innerMock
            .Setup(x => x.GetPlatformSummariesAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlatformSummary>>(summaries));

        await _sut.GetPlatformSummariesAsync(from, to, CancellationToken.None);
        await _sut.GetPlatformSummariesAsync(from, to, CancellationToken.None);

        _innerMock.Verify(
            x => x.GetPlatformSummariesAsync(from, to, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    private static DashboardSummary CreateTestSummary(int totalEngagement = 500) =>
        new(
            TotalEngagement: totalEngagement,
            PreviousEngagement: 400,
            TotalImpressions: 5000,
            PreviousImpressions: 4000,
            EngagementRate: 10m,
            PreviousEngagementRate: 10m,
            ContentPublished: 5,
            PreviousContentPublished: 4,
            CostPerEngagement: 0.05m,
            PreviousCostPerEngagement: 0.06m,
            WebsiteUsers: 200,
            PreviousWebsiteUsers: 180,
            GeneratedAt: DateTimeOffset.UtcNow);
}
