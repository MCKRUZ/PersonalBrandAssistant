using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.AnalyticsServices;

[Collection("Postgres")]
public class DashboardAggregatorIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _connectionString;
    private readonly Mock<IGoogleAnalyticsService> _mockGa = new();
    private ApplicationDbContext _dbContext = null!;
    private DashboardAggregator _sut = null!;

    // Test date range: 7-day window
    private static readonly DateTimeOffset Now = new(2026, 3, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = Now.AddDays(-6);
    private static readonly DateTimeOffset To = Now;

    public DashboardAggregatorIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.GetUniqueConnectionString();
    }

    public async Task InitializeAsync()
    {
        await using var masterCtx = _fixture.CreateDbContext(connectionString: _fixture.ConnectionString);
        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database!;
        // Safe: dbName is derived from Guid.NewGuid(), never user input. DDL cannot be parameterized.
#pragma warning disable EF1002
        await masterCtx.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{dbName}\"");
#pragma warning restore EF1002

        await using (var ctx = _fixture.CreateDbContext(connectionString: _connectionString))
        {
            await ctx.Database.MigrateAsync();
            await SeedTestDataAsync(ctx);
        }

        // Default GA4 mock: returns valid data
        _mockGa.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WebsiteOverview(120, 95, 340, 2.5, 45.0, 60)));

        _dbContext = _fixture.CreateDbContext(connectionString: _connectionString);
        _sut = new DashboardAggregator(_dbContext, _mockGa.Object, NullLogger<DashboardAggregator>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();

        await using var masterCtx = _fixture.CreateDbContext(connectionString: _fixture.ConnectionString);
        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database!;
        // Safe: dbName is derived from Guid.NewGuid(), never user input. DDL cannot be parameterized.
#pragma warning disable EF1002
        await masterCtx.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)");
#pragma warning restore EF1002
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesEngagementFromMultiplePlatforms()
    {
        var result = await _sut.GetSummaryAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Aggregator takes latest snapshot per ContentPlatformStatusId:
        // Twitter latest (Day 3): 15+5+4=24, YouTube (Day 2): 20+8+1=29. Total = 53
        Assert.Equal(53, result.Value!.TotalEngagement);
    }

    [Fact]
    public async Task GetSummaryAsync_PreviousPeriodComparison_CorrectDates()
    {
        var result = await _sut.GetSummaryAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Previous period: From-7 to From-1. Seeded snapshot at From-3: 5+2+1 = 8
        Assert.Equal(8, result.Value!.PreviousEngagement);
    }

    [Fact]
    public async Task GetSummaryAsync_IncludesGA4WebsiteUsers()
    {
        var result = await _sut.GetSummaryAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(120, result.Value!.WebsiteUsers);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenGA4Fails_ReturnsSocialDataWithZeroWebsiteUsers()
    {
        _mockGa.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WebsiteOverview>(ErrorCode.InternalError, "GA4 unavailable"));

        var result = await _sut.GetSummaryAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(53, result.Value!.TotalEngagement);
        Assert.Equal(0, result.Value.WebsiteUsers);
    }

    [Fact]
    public async Task GetTimelineAsync_GroupsByDateAndPlatform()
    {
        var result = await _sut.GetTimelineAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value!.Count); // 7 days in range
    }

    [Fact]
    public async Task GetTimelineAsync_FillsMissingDatesWithZeros()
    {
        var result = await _sut.GetTimelineAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Day 4 (From + 3) has no seeded data — should be zero-filled
        var day4 = result.Value![3];
        Assert.Equal(0, day4.Total);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_ReturnsPlatformsWithPostCounts()
    {
        var result = await _sut.GetPlatformSummariesAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var twitter = result.Value!.FirstOrDefault(p => p.Platform == PlatformType.TwitterX);
        Assert.NotNull(twitter);
        Assert.Equal(1, twitter.PostCount);
        Assert.True(twitter.IsAvailable);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_MarksLinkedInAsUnavailable()
    {
        var result = await _sut.GetPlatformSummariesAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var linkedin = result.Value!.FirstOrDefault(p => p.Platform == PlatformType.LinkedIn);
        Assert.NotNull(linkedin);
        Assert.False(linkedin.IsAvailable);
        Assert.Equal(0, linkedin.PostCount);
    }

    [Fact]
    public async Task GetPlatformSummariesAsync_CalculatesAvgEngagement()
    {
        var result = await _sut.GetPlatformSummariesAsync(From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var youtube = result.Value!.FirstOrDefault(p => p.Platform == PlatformType.YouTube);
        Assert.NotNull(youtube);
        // YouTube: 1 post, engagement = 20+8+1 = 29, avg = 29.0
        Assert.Equal(29.0, youtube.AvgEngagement);
    }

    private static async Task SeedTestDataAsync(ApplicationDbContext ctx)
    {
        // --- Content records ---
        var postA = Content.Create(ContentType.SocialPost, "Post A body", "Why agent frameworks need a rethink",
            [PlatformType.TwitterX]);
        postA.TransitionTo(ContentStatus.Review);
        postA.TransitionTo(ContentStatus.Approved);
        postA.TransitionTo(ContentStatus.Scheduled);
        postA.TransitionTo(ContentStatus.Publishing);
        postA.TransitionTo(ContentStatus.Published);
        postA.PublishedAt = From; // Day 1

        var postB = Content.Create(ContentType.SocialPost, "Post B body", "Building AI Agents",
            [PlatformType.YouTube]);
        postB.TransitionTo(ContentStatus.Review);
        postB.TransitionTo(ContentStatus.Approved);
        postB.TransitionTo(ContentStatus.Scheduled);
        postB.TransitionTo(ContentStatus.Publishing);
        postB.TransitionTo(ContentStatus.Published);
        postB.PublishedAt = From.AddDays(1); // Day 2

        // Previous period content
        var postC = Content.Create(ContentType.SocialPost, "Post C body", "Old post",
            [PlatformType.TwitterX]);
        postC.TransitionTo(ContentStatus.Review);
        postC.TransitionTo(ContentStatus.Approved);
        postC.TransitionTo(ContentStatus.Scheduled);
        postC.TransitionTo(ContentStatus.Publishing);
        postC.TransitionTo(ContentStatus.Published);
        postC.PublishedAt = From.AddDays(-3); // In previous period (From-7 to From-1)

        ctx.Contents.Add(postA);
        ctx.Contents.Add(postB);
        ctx.Contents.Add(postC);
        await ctx.SaveChangesAsync(CancellationToken.None);

        // --- ContentPlatformStatus records ---
        var twitterStatus = new ContentPlatformStatus
        {
            ContentId = postA.Id,
            Platform = PlatformType.TwitterX,
            Status = PlatformPublishStatus.Published,
            PublishedAt = From,
            PostUrl = "https://x.com/post/1",
        };

        var youtubeStatus = new ContentPlatformStatus
        {
            ContentId = postB.Id,
            Platform = PlatformType.YouTube,
            Status = PlatformPublishStatus.Published,
            PublishedAt = From.AddDays(1),
            PostUrl = "https://youtube.com/watch?v=1",
        };

        var prevTwitterStatus = new ContentPlatformStatus
        {
            ContentId = postC.Id,
            Platform = PlatformType.TwitterX,
            Status = PlatformPublishStatus.Published,
            PublishedAt = From.AddDays(-3),
            PostUrl = "https://x.com/post/old",
        };

        ctx.ContentPlatformStatuses.Add(twitterStatus);
        ctx.ContentPlatformStatuses.Add(youtubeStatus);
        ctx.ContentPlatformStatuses.Add(prevTwitterStatus);
        await ctx.SaveChangesAsync(CancellationToken.None);

        // --- EngagementSnapshot records ---
        // Twitter Day 1: likes=10, comments=3, shares=2
        ctx.EngagementSnapshots.Add(new EngagementSnapshot
        {
            ContentPlatformStatusId = twitterStatus.Id,
            Likes = 10, Comments = 3, Shares = 2,
            Impressions = 500, Clicks = 25,
            FetchedAt = From,
        });

        // Twitter Day 3: likes=15, comments=5, shares=4
        ctx.EngagementSnapshots.Add(new EngagementSnapshot
        {
            ContentPlatformStatusId = twitterStatus.Id,
            Likes = 15, Comments = 5, Shares = 4,
            Impressions = 800, Clicks = 40,
            FetchedAt = From.AddDays(2),
        });

        // YouTube Day 2: likes=20, comments=8, shares=1
        ctx.EngagementSnapshots.Add(new EngagementSnapshot
        {
            ContentPlatformStatusId = youtubeStatus.Id,
            Likes = 20, Comments = 8, Shares = 1,
            Impressions = 3200, Clicks = 150,
            FetchedAt = From.AddDays(1),
        });

        // Previous period snapshot: likes=5, comments=2, shares=1
        ctx.EngagementSnapshots.Add(new EngagementSnapshot
        {
            ContentPlatformStatusId = prevTwitterStatus.Id,
            Likes = 5, Comments = 2, Shares = 1,
            Impressions = 200, Clicks = 10,
            FetchedAt = From.AddDays(-3),
        });

        await ctx.SaveChangesAsync(CancellationToken.None);
    }
}
