using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

public class DashboardAggregatorTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IGoogleAnalyticsService> _ga = new();
    private readonly Mock<ILogger<DashboardAggregator>> _logger = new();

    private readonly DateTimeOffset _from = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly DateTimeOffset _to = new(2026, 3, 14, 23, 59, 59, TimeSpan.Zero);

    private DashboardAggregator CreateSut() => new(_db.Object, _ga.Object, _logger.Object);

    private static void SetEntityId<T>(T entity, Guid? id = null) where T : class
    {
        var prop = typeof(T).BaseType?.BaseType?.GetProperty("Id")
            ?? typeof(T).BaseType?.GetProperty("Id");
        prop?.SetValue(entity, id ?? Guid.NewGuid());
    }

    private static Content CreateContent(
        ContentStatus status = ContentStatus.Published,
        DateTimeOffset? publishedAt = null)
    {
        var content = Content.Create(ContentType.SocialPost, "body", "Test Post");
        // Use reflection to set Status since TransitionTo has state machine rules
        typeof(Content).GetProperty("Status")!.SetValue(content, status);
        content.PublishedAt = publishedAt;
        return content;
    }

    private static ContentPlatformStatus CreateStatus(
        Guid contentId,
        PlatformType platform,
        DateTimeOffset? publishedAt = null)
    {
        var cps = new ContentPlatformStatus
        {
            ContentId = contentId,
            Platform = platform,
            Status = PlatformPublishStatus.Published,
            PublishedAt = publishedAt,
            PostUrl = $"https://example.com/{platform}/{contentId}"
        };
        SetEntityId(cps);
        return cps;
    }

    private static EngagementSnapshot CreateSnapshot(
        Guid contentPlatformStatusId,
        int likes = 10, int comments = 5, int shares = 3,
        int? impressions = 100,
        DateTimeOffset? fetchedAt = null)
    {
        var snapshot = new EngagementSnapshot
        {
            ContentPlatformStatusId = contentPlatformStatusId,
            Likes = likes,
            Comments = comments,
            Shares = shares,
            Impressions = impressions,
            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
        };
        SetEntityId(snapshot);
        return snapshot;
    }

    private static AgentExecution CreateExecution(Guid contentId, decimal cost)
    {
        var exec = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard, contentId);
        exec.MarkRunning();
        exec.RecordUsage("claude-3", 100, 200, 0, 0, cost);
        exec.Complete();
        return exec;
    }

    private void SetupDbSets(
        Content[]? contents = null,
        ContentPlatformStatus[]? statuses = null,
        EngagementSnapshot[]? snapshots = null,
        AgentExecution[]? executions = null)
    {
        _db.Setup(d => d.Contents).Returns((contents ?? []).AsQueryable().BuildMockDbSet().Object);
        _db.Setup(d => d.ContentPlatformStatuses).Returns((statuses ?? []).AsQueryable().BuildMockDbSet().Object);
        _db.Setup(d => d.EngagementSnapshots).Returns((snapshots ?? []).AsQueryable().BuildMockDbSet().Object);
        _db.Setup(d => d.AgentExecutions).Returns((executions ?? []).AsQueryable().BuildMockDbSet().Object);
    }

    private void SetupGaSuccess(int activeUsers = 100, int pageViews = 500)
    {
        _ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WebsiteOverview(activeUsers, 200, pageViews, 120.0, 0.45, 80)));
    }

    private void SetupGaFailure()
    {
        _ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WebsiteOverview>(ErrorCode.InternalError, "GA4 unavailable"));
    }

    // ── GetSummaryAsync ──

    [Fact]
    public async Task GetSummaryAsync_ReturnsTotalEngagement_SummingLikesCommentsShares()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps1 = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        var cps2 = CreateStatus(content.Id, PlatformType.YouTube, _from.AddDays(1));
        var snap1 = CreateSnapshot(cps1.Id, likes: 20, comments: 10, shares: 5, fetchedAt: _from.AddDays(2));
        var snap2 = CreateSnapshot(cps2.Id, likes: 30, comments: 15, shares: 10, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps1, cps2],
            snapshots: [snap1, snap2]);
        SetupGaSuccess();

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // (20+10+5) + (30+15+10) = 90
        Assert.Equal(90, result.Value!.TotalEngagement);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsTotalImpressions_IncludingGa4PageViews()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        var snap = CreateSnapshot(cps.Id, impressions: 200, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);
        SetupGaSuccess(pageViews: 800);

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Social impressions (200) + GA4 page views (800) = 1000
        Assert.Equal(1000, result.Value!.TotalImpressions);
    }

    [Fact]
    public async Task GetSummaryAsync_CalculatesEngagementRate_CorrectlyAsPercentage()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        // 10+5+3 = 18 engagement, 100 social impressions
        var snap = CreateSnapshot(cps.Id, likes: 10, comments: 5, shares: 3, impressions: 100, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);
        // GA4 adds 400 page views to impressions => total impressions = 500
        SetupGaSuccess(pageViews: 400);

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // 18 / 500 * 100 = 3.6%
        Assert.Equal(3.6m, result.Value!.EngagementRate);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns0EngagementRate_WhenImpressionsIs0()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        var snap = CreateSnapshot(cps.Id, impressions: null, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);
        SetupGaFailure();

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.EngagementRate);
    }

    [Fact]
    public async Task GetSummaryAsync_CalculatesPreviousPeriod_WithCorrectDateOffset()
    {
        // Current: Mar 1-14 (14 days). Previous: Feb 15-28 (14 days)
        var prevFrom = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

        var currentContent = CreateContent(publishedAt: _from.AddDays(1));
        var currentCps = CreateStatus(currentContent.Id, PlatformType.TwitterX, _from.AddDays(1));
        var currentSnap = CreateSnapshot(currentCps.Id, likes: 50, comments: 20, shares: 10, fetchedAt: _from.AddDays(2));

        var prevContent = CreateContent(publishedAt: prevFrom.AddDays(1));
        var prevCps = CreateStatus(prevContent.Id, PlatformType.TwitterX, prevFrom.AddDays(1));
        var prevSnap = CreateSnapshot(prevCps.Id, likes: 30, comments: 10, shares: 5, fetchedAt: prevFrom.AddDays(2));

        SetupDbSets(
            contents: [currentContent, prevContent],
            statuses: [currentCps, prevCps],
            snapshots: [currentSnap, prevSnap]);
        SetupGaSuccess();

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(80, result.Value!.TotalEngagement); // 50+20+10
        Assert.Equal(45, result.Value.PreviousEngagement); // 30+10+5
    }

    [Fact]
    public async Task GetSummaryAsync_Returns0PreviousEngagement_WhenNoPreviousData()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        var snap = CreateSnapshot(cps.Id, likes: 50, comments: 20, shares: 10, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);
        SetupGaSuccess();

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PreviousEngagement);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsPartialData_WhenGa4Fails()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        var snap = CreateSnapshot(cps.Id, likes: 20, comments: 10, shares: 5, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);
        SetupGaFailure();

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(35, result.Value!.TotalEngagement); // 20+10+5
        Assert.Equal(0, result.Value.WebsiteUsers);
    }

    [Fact]
    public async Task GetSummaryAsync_IncludesWebsiteUsers_FromGa4Overview()
    {
        SetupDbSets();
        SetupGaSuccess(activeUsers: 1500);

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1500, result.Value!.WebsiteUsers);
    }

    [Fact]
    public async Task GetSummaryAsync_CalculatesCostPerEngagement_FromAgentExecutions()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from.AddDays(1));
        // 10+5+3 = 18 engagement
        var snap = CreateSnapshot(cps.Id, likes: 10, comments: 5, shares: 3, fetchedAt: _from.AddDays(2));
        var exec = CreateExecution(content.Id, 0.36m);

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap],
            executions: [exec]);
        SetupGaSuccess();

        var result = await CreateSut().GetSummaryAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // 0.36 / 18 = 0.02
        Assert.Equal(0.02m, result.Value!.CostPerEngagement);
    }

    // ── GetTimelineAsync ──

    [Fact]
    public async Task GetTimelineAsync_GroupsDailyEngagement_ByPlatformWithCorrectBreakdown()
    {
        var content = CreateContent(publishedAt: _from);
        var twitterCps = CreateStatus(content.Id, PlatformType.TwitterX, _from);
        var ytCps = CreateStatus(content.Id, PlatformType.YouTube, _from);

        var day1 = _from.AddDays(1);
        var day2 = _from.AddDays(2);

        var snaps = new[]
        {
            CreateSnapshot(twitterCps.Id, likes: 10, comments: 5, shares: 2, fetchedAt: day1),
            CreateSnapshot(ytCps.Id, likes: 20, comments: 8, shares: 4, fetchedAt: day1),
            CreateSnapshot(twitterCps.Id, likes: 15, comments: 7, shares: 3, fetchedAt: day2),
        };

        SetupDbSets(
            contents: [content],
            statuses: [twitterCps, ytCps],
            snapshots: snaps);

        var result = await CreateSut().GetTimelineAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var timeline = result.Value!;
        // Should have entries for each day in range (14 days for Mar 1-14)
        Assert.Equal(14, timeline.Count);

        var mar2 = timeline.First(d => d.Date == new DateOnly(2026, 3, 2));
        Assert.Equal(2, mar2.Platforms.Count); // Twitter + YouTube
        var twitter = mar2.Platforms.First(p => p.Platform == PlatformType.TwitterX);
        Assert.Equal(10, twitter.Likes);
        Assert.Equal(5, twitter.Comments);
        Assert.Equal(2, twitter.Shares);
    }

    [Fact]
    public async Task GetTimelineAsync_FillsMissingDates_WithZeroValues()
    {
        var content = CreateContent(publishedAt: _from);
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, _from);

        // Only data for day 1 and day 3
        var snaps = new[]
        {
            CreateSnapshot(cps.Id, likes: 10, comments: 5, shares: 2, fetchedAt: _from.AddDays(1)),
            CreateSnapshot(cps.Id, likes: 20, comments: 8, shares: 4, fetchedAt: _from.AddDays(3)),
        };

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: snaps);

        var result = await CreateSut().GetTimelineAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Day 2 (Mar 3) should have zero-valued entry
        var mar3 = result.Value!.First(d => d.Date == new DateOnly(2026, 3, 3));
        Assert.Equal(0, mar3.Total);
    }

    [Fact]
    public async Task GetTimelineAsync_HandlesSingleDayRange()
    {
        var singleDay = _from;
        var content = CreateContent(publishedAt: singleDay);
        var cps = CreateStatus(content.Id, PlatformType.TwitterX, singleDay);
        var snap = CreateSnapshot(cps.Id, likes: 5, comments: 2, shares: 1, fetchedAt: singleDay.AddHours(6));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);

        var result = await CreateSut().GetTimelineAsync(singleDay, singleDay.AddHours(23), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var timeline = result.Value!;
        Assert.Single(timeline);
        Assert.Equal(8, timeline[0].Total); // 5+2+1
    }

    [Fact]
    public async Task GetTimelineAsync_Handles90DayRange_ReturningCorrectEntryCount()
    {
        var from90 = _from;
        var to90 = _from.AddDays(89);

        SetupDbSets(); // empty data

        var result = await CreateSut().GetTimelineAsync(from90, to90, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(90, result.Value!.Count);
    }

    // ── GetPlatformSummariesAsync ──

    [Fact]
    public async Task GetPlatformSummariesAsync_ReturnsSummaryPerPlatform_WithCorrectPostCount()
    {
        var c1 = CreateContent(publishedAt: _from.AddDays(1));
        var c2 = CreateContent(publishedAt: _from.AddDays(2));
        var tCps1 = CreateStatus(c1.Id, PlatformType.TwitterX, _from.AddDays(1));
        var tCps2 = CreateStatus(c2.Id, PlatformType.TwitterX, _from.AddDays(2));
        var yCps = CreateStatus(c1.Id, PlatformType.YouTube, _from.AddDays(1));

        SetupDbSets(
            contents: [c1, c2],
            statuses: [tCps1, tCps2, yCps],
            snapshots: [
                CreateSnapshot(tCps1.Id, fetchedAt: _from.AddDays(2)),
                CreateSnapshot(tCps2.Id, fetchedAt: _from.AddDays(3)),
                CreateSnapshot(yCps.Id, fetchedAt: _from.AddDays(2)),
            ]);

        var result = await CreateSut().GetPlatformSummariesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summaries = result.Value!;
        var twitter = summaries.First(p => p.Platform == PlatformType.TwitterX);
        Assert.Equal(2, twitter.PostCount);
        var youtube = summaries.First(p => p.Platform == PlatformType.YouTube);
        Assert.Equal(1, youtube.PostCount);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_CalculatesAvgEngagementPerPost()
    {
        var c1 = CreateContent(publishedAt: _from.AddDays(1));
        var c2 = CreateContent(publishedAt: _from.AddDays(2));
        var cps1 = CreateStatus(c1.Id, PlatformType.TwitterX, _from.AddDays(1));
        var cps2 = CreateStatus(c2.Id, PlatformType.TwitterX, _from.AddDays(2));

        // Post 1: 10+5+3=18, Post 2: 4+3+3=10 -> avg = 14
        SetupDbSets(
            contents: [c1, c2],
            statuses: [cps1, cps2],
            snapshots: [
                CreateSnapshot(cps1.Id, likes: 10, comments: 5, shares: 3, fetchedAt: _from.AddDays(2)),
                CreateSnapshot(cps2.Id, likes: 4, comments: 3, shares: 3, fetchedAt: _from.AddDays(3)),
            ]);

        var result = await CreateSut().GetPlatformSummariesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var twitter = result.Value!.First(p => p.Platform == PlatformType.TwitterX);
        Assert.Equal(14.0, twitter.AvgEngagement);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_IdentifiesTopPerformingPost()
    {
        var c1 = CreateContent(publishedAt: _from.AddDays(1));
        c1.Title = "Viral Post";
        var c2 = CreateContent(publishedAt: _from.AddDays(2));
        c2.Title = "Normal Post";

        var cps1 = CreateStatus(c1.Id, PlatformType.TwitterX, _from.AddDays(1));
        var cps2 = CreateStatus(c2.Id, PlatformType.TwitterX, _from.AddDays(2));

        // Post 1 has higher engagement
        SetupDbSets(
            contents: [c1, c2],
            statuses: [cps1, cps2],
            snapshots: [
                CreateSnapshot(cps1.Id, likes: 50, comments: 20, shares: 10, fetchedAt: _from.AddDays(2)),
                CreateSnapshot(cps2.Id, likes: 5, comments: 2, shares: 1, fetchedAt: _from.AddDays(3)),
            ]);

        var result = await CreateSut().GetPlatformSummariesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var twitter = result.Value!.First(p => p.Platform == PlatformType.TwitterX);
        Assert.Equal("Viral Post", twitter.TopPostTitle);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_MarksLinkedInAsUnavailable()
    {
        SetupDbSets();

        var result = await CreateSut().GetPlatformSummariesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var linkedin = result.Value!.FirstOrDefault(p => p.Platform == PlatformType.LinkedIn);
        Assert.NotNull(linkedin);
        Assert.False(linkedin!.IsAvailable);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_ReturnsNullFollowerCount()
    {
        var content = CreateContent(publishedAt: _from.AddDays(1));
        var cps = CreateStatus(content.Id, PlatformType.Reddit, _from.AddDays(1));
        var snap = CreateSnapshot(cps.Id, fetchedAt: _from.AddDays(2));

        SetupDbSets(
            contents: [content],
            statuses: [cps],
            snapshots: [snap]);

        var result = await CreateSut().GetPlatformSummariesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reddit = result.Value!.First(p => p.Platform == PlatformType.Reddit);
        Assert.Null(reddit.FollowerCount);
    }
}
