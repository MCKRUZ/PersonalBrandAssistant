using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

public class EngagementAggregatorTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ISocialPlatform> _twitterPlatform = new();
    private readonly Mock<ISocialPlatform> _linkedInPlatform = new();
    private readonly Mock<IRateLimiter> _rateLimiter = new();
    private readonly Mock<ILogger<EngagementAggregator>> _logger = new();
    private readonly ContentEngineOptions _options = new();

    public EngagementAggregatorTests()
    {
        _twitterPlatform.Setup(p => p.Type).Returns(PlatformType.TwitterX);
        _linkedInPlatform.Setup(p => p.Type).Returns(PlatformType.LinkedIn);
    }

    private EngagementAggregator CreateSut(IEnumerable<ISocialPlatform>? platforms = null) => new(
        _db.Object,
        platforms ?? [_twitterPlatform.Object, _linkedInPlatform.Object],
        _rateLimiter.Object,
        Options.Create(_options),
        _logger.Object);

    private static ContentPlatformStatus CreateStatus(
        Guid contentId,
        PlatformType platform,
        string? platformPostId = "post-123",
        PlatformPublishStatus status = PlatformPublishStatus.Published)
    {
        var cps = new ContentPlatformStatus
        {
            ContentId = contentId,
            Platform = platform,
            Status = status,
            PlatformPostId = platformPostId,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        // Set Id via reflection since it's from EntityBase
        typeof(ContentPlatformStatus).BaseType!.BaseType!
            .GetProperty("Id")!.SetValue(cps, Guid.NewGuid());
        return cps;
    }

    private static EngagementSnapshot CreateSnapshot(
        Guid contentPlatformStatusId,
        int likes = 10,
        int comments = 5,
        int shares = 3,
        DateTimeOffset? fetchedAt = null)
    {
        var snapshot = new EngagementSnapshot
        {
            ContentPlatformStatusId = contentPlatformStatusId,
            Likes = likes,
            Comments = comments,
            Shares = shares,
            Impressions = 100,
            Clicks = 50,
            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
        };
        typeof(EngagementSnapshot).BaseType!.BaseType!
            .GetProperty("Id")!.SetValue(snapshot, Guid.NewGuid());
        return snapshot;
    }

    private void SetupDbSets(
        ContentPlatformStatus[]? statuses = null,
        EngagementSnapshot[]? snapshots = null,
        AgentExecution[]? executions = null,
        Content[]? contents = null)
    {
        var statusMock = (statuses ?? []).AsQueryable().BuildMockDbSet();
        _db.Setup(d => d.ContentPlatformStatuses).Returns(statusMock.Object);

        var snapshotMock = (snapshots ?? []).AsQueryable().BuildMockDbSet();
        _db.Setup(d => d.EngagementSnapshots).Returns(snapshotMock.Object);

        var executionMock = (executions ?? []).AsQueryable().BuildMockDbSet();
        _db.Setup(d => d.AgentExecutions).Returns(executionMock.Object);

        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
        _db.Setup(d => d.Contents).Returns(contentMock.Object);
    }

    // ── FetchLatestAsync ──

    [Fact]
    public async Task FetchLatestAsync_ValidContentPlatformStatus_CallsGetEngagementAndSavesSnapshot()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tweet-456");

        SetupDbSets(statuses: [cps]);

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));

        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var stats = new EngagementStats(10, 5, 3, 100, 50, new Dictionary<string, int>());
        _twitterPlatform.Setup(p => p.GetEngagementAsync("tweet-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(stats));

        var sut = CreateSut();

        // Act
        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.Likes);
        Assert.Equal(5, result.Value.Comments);
        Assert.Equal(3, result.Value.Shares);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchLatestAsync_NoPlatformPostId_ReturnsValidationError()
    {
        // Arrange
        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, platformPostId: null);
        SetupDbSets(statuses: [cps]);

        var sut = CreateSut();

        // Act
        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task FetchLatestAsync_RateLimited_ReturnsError()
    {
        // Arrange
        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, "tweet-789");
        SetupDbSets(statuses: [cps]);

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(false, DateTimeOffset.UtcNow.AddMinutes(5), "Rate limit exceeded")));

        var sut = CreateSut();

        // Act
        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.RateLimited, result.ErrorCode);
        _twitterPlatform.Verify(p => p.GetEngagementAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchLatestAsync_NoPlatformAdapter_ReturnsInternalError()
    {
        // Arrange
        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, "tweet-no-adapter");
        SetupDbSets(statuses: [cps]);

        var sut = CreateSut(platforms: []);

        // Act
        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
    }

    [Fact]
    public async Task FetchLatestAsync_PlatformApiError_ReturnsErrorResult()
    {
        // Arrange
        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, "tweet-fail");
        SetupDbSets(statuses: [cps]);

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));

        _twitterPlatform.Setup(p => p.GetEngagementAsync("tweet-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<EngagementStats>(ErrorCode.InternalError, "API error"));

        var sut = CreateSut();

        // Act
        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchLatestAsync_NotFound_ReturnsNotFoundError()
    {
        // Arrange
        SetupDbSets(statuses: []);

        var sut = CreateSut();

        // Act
        var result = await sut.FetchLatestAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // ── GetPerformanceAsync ──

    [Fact]
    public async Task GetPerformanceAsync_MultiPlatformContent_AggregatesCorrectly()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var twitterCps = CreateStatus(contentId, PlatformType.TwitterX, "tw-1");
        var linkedInCps = CreateStatus(contentId, PlatformType.LinkedIn, "li-1");

        var twitterSnapshot = CreateSnapshot(twitterCps.Id, likes: 10, comments: 5, shares: 3);
        var linkedInSnapshot = CreateSnapshot(linkedInCps.Id, likes: 20, comments: 10, shares: 7);

        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard, contentId);
        execution.MarkRunning();
        execution.RecordUsage("claude-3", 1000, 500, 0, 0, 0.05m);
        execution.Complete();

        SetupDbSets(
            statuses: [twitterCps, linkedInCps],
            snapshots: [twitterSnapshot, linkedInSnapshot],
            executions: [execution]);

        var sut = CreateSut();

        // Act
        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var report = result.Value!;
        Assert.Equal(contentId, report.ContentId);
        Assert.Equal(2, report.LatestByPlatform.Count);
        // Total = (10+5+3) + (20+10+7) = 55
        Assert.Equal(55, report.TotalEngagement);
        Assert.Equal(0.05m, report.LlmCost);
        Assert.NotNull(report.CostPerEngagement);
    }

    [Fact]
    public async Task GetPerformanceAsync_ZeroEngagement_CostPerEngagementIsNull()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tw-zero");
        var snapshot = CreateSnapshot(cps.Id, likes: 0, comments: 0, shares: 0);

        SetupDbSets(
            statuses: [cps],
            snapshots: [snapshot],
            executions: []);

        var sut = CreateSut();

        // Act
        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalEngagement);
        Assert.Null(result.Value.CostPerEngagement);
    }

    [Fact]
    public async Task GetPerformanceAsync_NoAgentExecutions_LlmCostIsNull()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tw-nocost");
        var snapshot = CreateSnapshot(cps.Id, likes: 10, comments: 5, shares: 3);

        SetupDbSets(
            statuses: [cps],
            snapshots: [snapshot],
            executions: []);

        var sut = CreateSut();

        // Act
        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.LlmCost);
        Assert.Null(result.Value.CostPerEngagement);
    }

    // ── GetTopContentAsync ──

    [Fact]
    public async Task GetTopContentAsync_ReturnsOrderedByTotalEngagement()
    {
        // Arrange
        var content1Id = Guid.NewGuid();
        var content2Id = Guid.NewGuid();
        var content3Id = Guid.NewGuid();

        var content1 = Content.Create(ContentType.BlogPost, "body1", "Low Performer");
        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content1, content1Id);

        var content2 = Content.Create(ContentType.BlogPost, "body2", "Top Performer");
        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content2, content2Id);

        var content3 = Content.Create(ContentType.BlogPost, "body3", "Mid Performer");
        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content3, content3Id);

        var cps1 = CreateStatus(content1Id, PlatformType.TwitterX, "tw-low");
        var cps2 = CreateStatus(content2Id, PlatformType.TwitterX, "tw-top");
        var cps3 = CreateStatus(content3Id, PlatformType.TwitterX, "tw-mid");

        var snap1 = CreateSnapshot(cps1.Id, likes: 2, comments: 1, shares: 0); // Total: 3
        var snap2 = CreateSnapshot(cps2.Id, likes: 50, comments: 25, shares: 15); // Total: 90
        var snap3 = CreateSnapshot(cps3.Id, likes: 10, comments: 5, shares: 3); // Total: 18

        SetupDbSets(
            statuses: [cps1, cps2, cps3],
            snapshots: [snap1, snap2, snap3],
            contents: [content1, content2, content3]);

        var sut = CreateSut();

        // Act
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;
        var result = await sut.GetTopContentAsync(from, to, limit: 2, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("Top Performer", result.Value[0].Title);
        Assert.Equal(90, result.Value[0].TotalEngagement);
        Assert.Equal("Mid Performer", result.Value[1].Title);
        Assert.Equal(18, result.Value[1].TotalEngagement);
    }

    [Fact]
    public async Task GetTopContentAsync_UsesLatestSnapshotPerPlatform()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var content = Content.Create(ContentType.BlogPost, "body", "Test Content");
        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content, contentId);

        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tw-multi");

        var oldSnapshot = CreateSnapshot(cps.Id, likes: 5, comments: 2, shares: 1,
            fetchedAt: DateTimeOffset.UtcNow.AddHours(-6));
        var newSnapshot = CreateSnapshot(cps.Id, likes: 50, comments: 20, shares: 10,
            fetchedAt: DateTimeOffset.UtcNow);

        SetupDbSets(
            statuses: [cps],
            snapshots: [oldSnapshot, newSnapshot],
            contents: [content]);

        var sut = CreateSut();

        // Act
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;
        var result = await sut.GetTopContentAsync(from, to, limit: 10, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        // Should use latest snapshot: 50+20+10 = 80, not old: 5+2+1 = 8
        Assert.Equal(80, result.Value![0].TotalEngagement);
    }

    [Fact]
    public async Task GetTopContentAsync_EmptyRange_ReturnsEmptyList()
    {
        // Arrange
        SetupDbSets(statuses: [], snapshots: [], contents: []);

        var sut = CreateSut();

        // Act
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;
        var result = await sut.GetTopContentAsync(from, to, limit: 10, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetTopContentAsync_InvalidLimit_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetTopContentAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, limit: 0, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task GetTopContentAsync_FromAfterTo_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetTopContentAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), limit: 10, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    // ── GetPerformanceAsync edge cases ──

    [Fact]
    public async Task GetPerformanceAsync_NoPublishedStatuses_ReturnsEmptyReport()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        SetupDbSets(statuses: [], snapshots: [], executions: []);

        var sut = CreateSut();

        // Act
        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.LatestByPlatform);
        Assert.Equal(0, result.Value.TotalEngagement);
        Assert.Null(result.Value.LlmCost);
        Assert.Null(result.Value.CostPerEngagement);
    }
}
