using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Queries;

public class GetFeedSummaryHandlerTests
{
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
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.ApprovalRequest, isActedOn: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isActedOn: true));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isActedOn: false));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.PendingApprovals);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectTrendingCount()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 3; i++)
            context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: true));
        for (var i = 0; i < 2; i++)
            context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isRead: false));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TrendingCount);
    }

    [Fact]
    public async Task Handle_CalculatesEngagementDelta_FromAnalyticsHighlightItemsInLast24h()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AnalyticsHighlight,
            createdAt: DateTimeOffset.UtcNow.AddHours(-2),
            data: @"{""delta"":25.0}"));
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AnalyticsHighlight,
            createdAt: DateTimeOffset.UtcNow.AddHours(-6),
            data: @"{""delta"":15.0}"));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20.0, result.Value!.EngagementDelta, precision: 1);
    }

    [Fact]
    public async Task Handle_ReturnsZeroEngagementDelta_WhenNoAnalyticsHighlightItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.0, result.Value!.EngagementDelta);
    }

    [Fact]
    public async Task Handle_ReturnsAllZeros_WhenFeedIsEmpty()
    {
        await using var context = CreateContext();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = result.Value!;
        Assert.Equal(0, summary.UnreadCount);
        Assert.Equal(0, summary.PendingApprovals);
        Assert.Equal(0, summary.TrendingCount);
        Assert.Equal(0.0, summary.EngagementDelta);
    }

    [Fact]
    public async Task Handle_ExcludesExpiredItemsFromAllCounts()
    {
        await using var context = CreateContext();
        var expired = DateTimeOffset.UtcNow.AddHours(-1);

        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.SystemNotification, isRead: false, expiresAt: expired));
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AgentDraft, isActedOn: false, expiresAt: expired));
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.TrendAlert, isRead: false, expiresAt: expired));
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AnalyticsHighlight,
            createdAt: DateTimeOffset.UtcNow.AddHours(-2),
            data: @"{""delta"":50.0}",
            expiresAt: expired));
        await context.SaveChangesAsync();

        var handler = new GetFeedSummary.Handler(context);
        var result = await handler.Handle(new GetFeedSummary.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = result.Value!;
        Assert.Equal(0, summary.UnreadCount);
        Assert.Equal(0, summary.PendingApprovals);
        Assert.Equal(0, summary.TrendingCount);
        Assert.Equal(0.0, summary.EngagementDelta);
    }
}
