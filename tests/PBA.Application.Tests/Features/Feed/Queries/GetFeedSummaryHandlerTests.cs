using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Queries;

public class GetFeedSummaryHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FeedItem CreateFeedItem(
        FeedItemType type = FeedItemType.SystemNotification,
        bool isRead = false,
        bool isActedOn = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        string? data = null)
    {
        return new FeedItem
        {
            Title = "Test",
            Type = type,
            IsRead = isRead,
            IsActedOn = isActedOn,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            Data = data
        };
    }

    [Fact]
    public async Task Handle_ReturnsCorrectUnreadCount()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 5; i++)
            context.FeedItems.Add(CreateFeedItem(isRead: false));
        for (var i = 0; i < 3; i++)
            context.FeedItems.Add(CreateFeedItem(isRead: true));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.UnreadCount);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectPendingApprovals()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isActedOn: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isActedOn: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.ApprovalRequest, isActedOn: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isActedOn: true));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isActedOn: false));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.PendingApprovals);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectTrendingCount()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 4; i++)
            context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: false));
        for (var i = 0; i < 2; i++)
            context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: true));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.TrendingCount);
    }

    [Fact]
    public async Task Handle_CalculatesEngagementDeltaFromLast24Hours()
    {
        await using var context = CreateContext();
        var recentData = new[] { 10.0, 20.0, 30.0 };
        foreach (var delta in recentData)
        {
            context.FeedItems.Add(CreateFeedItem(
                type: FeedItemType.AnalyticsHighlight,
                createdAt: DateTimeOffset.UtcNow.AddHours(-1),
                data: $@"{{""metric"":""impressions"",""currentValue"":500,""previousValue"":400,""delta"":{delta}}}"));
        }
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AnalyticsHighlight,
            createdAt: DateTimeOffset.UtcNow.AddHours(-48),
            data: @"{""metric"":""impressions"",""currentValue"":100,""previousValue"":50,""delta"":100.0}"));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20.0, result.Value!.EngagementDelta, precision: 1);
    }

    [Fact]
    public async Task Handle_ReturnsZeroEngagementDeltaWhenNoAnalyticsItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.EngagementDelta);
    }

    [Fact]
    public async Task Handle_EmptyFeed_ReturnsAllZeros()
    {
        await using var context = CreateContext();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.UnreadCount);
        Assert.Equal(0, result.Value.PendingApprovals);
        Assert.Equal(0, result.Value.TrendingCount);
        Assert.Equal(0, result.Value.EngagementDelta);
    }

    [Fact]
    public async Task Handle_ExcludesExpiredItemsFromAllCounts()
    {
        await using var context = CreateContext();
        var expired = DateTimeOffset.UtcNow.AddHours(-1);
        context.FeedItems.Add(CreateFeedItem(isRead: false, expiresAt: expired));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isActedOn: false, expiresAt: expired));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: false, expiresAt: expired));
        context.FeedItems.Add(CreateFeedItem(isRead: false));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.UnreadCount);
        Assert.Equal(0, result.Value.PendingApprovals);
        Assert.Equal(0, result.Value.TrendingCount);
    }
}
