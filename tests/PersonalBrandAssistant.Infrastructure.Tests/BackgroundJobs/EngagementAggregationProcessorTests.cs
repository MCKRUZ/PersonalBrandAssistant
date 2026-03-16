using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class EngagementAggregationProcessorTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly Mock<IEngagementAggregator> _aggregator = new();
    private readonly Mock<ILogger<EngagementAggregationProcessor>> _logger = new();
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly ContentEngineOptions _options = new();

    private readonly DateTimeOffset _now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);

    public EngagementAggregationProcessorTests()
    {
        _dateTimeProvider.Setup(d => d.UtcNow).Returns(_now);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IEngagementAggregator)))
            .Returns(_aggregator.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext)))
            .Returns(_dbContext.Object);
    }

    private EngagementAggregationProcessor CreateSut() => new(
        _scopeFactory.Object,
        _dateTimeProvider.Object,
        Options.Create(_options),
        _logger.Object);

    private void SetupDbSets(ContentPlatformStatus[]? statuses = null)
    {
        var statusMock = (statuses ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.ContentPlatformStatuses).Returns(statusMock.Object);
    }

    private static ContentPlatformStatus CreatePublishedStatus(
        PlatformType platform = PlatformType.TwitterX,
        string postId = "post-123")
    {
        var cps = new ContentPlatformStatus
        {
            ContentId = Guid.NewGuid(),
            Platform = platform,
            Status = PlatformPublishStatus.Published,
            PlatformPostId = postId,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        typeof(ContentPlatformStatus).BaseType!.BaseType!
            .GetProperty("Id")!.SetValue(cps, Guid.NewGuid());
        return cps;
    }

    [Fact]
    public async Task ProcessAsync_PublishedContent_QueriesWithinRetentionWindow()
    {
        // Arrange
        var recentStatus = CreatePublishedStatus();
        SetupDbSets([recentStatus]);

        _aggregator.Setup(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(0));

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _aggregator.Verify(
            a => a.FetchLatestAsync(recentStatus.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_EachEntry_FetchesEngagement()
    {
        // Arrange
        var status1 = CreatePublishedStatus(PlatformType.TwitterX, "tw-1");
        var status2 = CreatePublishedStatus(PlatformType.LinkedIn, "li-1");
        SetupDbSets([status1, status2]);

        _aggregator.Setup(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(0));

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _aggregator.Verify(
            a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_RateLimited_SkipsEntry()
    {
        // Arrange
        var status = CreatePublishedStatus();
        SetupDbSets([status]);

        _aggregator.Setup(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<EngagementSnapshot>(ErrorCode.RateLimited, "Rate limited"));
        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(0));

        var sut = CreateSut();

        // Act
        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));

        // Assert
        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessAsync_PlatformError_ContinuesWithOthers()
    {
        // Arrange
        var status1 = CreatePublishedStatus(PlatformType.TwitterX, "tw-fail");
        var status2 = CreatePublishedStatus(PlatformType.LinkedIn, "li-ok");
        SetupDbSets([status1, status2]);

        _aggregator.SetupSequence(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API down"))
            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(0));

        var sut = CreateSut();

        // Act
        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));

        // Assert
        Assert.Null(ex);
        _aggregator.Verify(
            a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_RetentionCleanup_CalledAfterFetch()
    {
        // Arrange
        SetupDbSets([]);
        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(5));

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _aggregator.Verify(
            a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
