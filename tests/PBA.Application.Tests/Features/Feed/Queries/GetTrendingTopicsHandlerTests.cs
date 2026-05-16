using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Queries;

public class GetTrendingTopicsHandlerTests
{
    private static FeedItem CreateTrendAlert(string topic, DateTimeOffset? createdAt = null) =>
        CreateFeedItem(
            type: FeedItemType.TrendAlert,
            data: $@"{{""topic"":""{topic}"",""source"":""Twitter"",""mentionCount"":10,""sentiment"":""positive""}}",
            createdAt: createdAt);

    [Fact]
    public async Task Handle_ReturnsTopicsGroupedByTopicField()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 3; i++)
            context.FeedItems.Add(CreateTrendAlert("Claude Code"));
        for (var i = 0; i < 2; i++)
            context.FeedItems.Add(CreateTrendAlert("AI Agents"));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var topics = result.Value!;
        Assert.Equal(2, topics.Count);
        Assert.Contains(topics, t => t.Topic == "Claude Code" && t.Count == 3);
        Assert.Contains(topics, t => t.Topic == "AI Agents" && t.Count == 2);
    }

    [Fact]
    public async Task Handle_OrdersByCountDescending()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateTrendAlert("Less Popular"));
        for (var i = 0; i < 5; i++)
            context.FeedItems.Add(CreateTrendAlert("Most Popular"));
        for (var i = 0; i < 3; i++)
            context.FeedItems.Add(CreateTrendAlert("Mid Popular"));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var topics = result.Value!;
        Assert.Equal("Most Popular", topics[0].Topic);
        Assert.Equal(5, topics[0].Count);
        Assert.Equal("Mid Popular", topics[1].Topic);
        Assert.Equal(3, topics[1].Count);
        Assert.Equal("Less Popular", topics[2].Topic);
        Assert.Equal(1, topics[2].Count);
    }

    [Fact]
    public async Task Handle_LimitsToTop10Results()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 15; i++)
            context.FeedItems.Add(CreateTrendAlert($"Topic {i}"));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.Count);
    }

    [Fact]
    public async Task Handle_OnlyConsidersLast7Days()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateTrendAlert("Recent", createdAt: DateTimeOffset.UtcNow.AddDays(-1)));
        context.FeedItems.Add(CreateTrendAlert("Recent", createdAt: DateTimeOffset.UtcNow.AddDays(-3)));
        context.FeedItems.Add(CreateTrendAlert("Old", createdAt: DateTimeOffset.UtcNow.AddDays(-10)));
        context.FeedItems.Add(CreateTrendAlert("Old", createdAt: DateTimeOffset.UtcNow.AddDays(-10)));
        context.FeedItems.Add(CreateTrendAlert("Old", createdAt: DateTimeOffset.UtcNow.AddDays(-10)));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var topics = result.Value!;
        Assert.Single(topics);
        Assert.Equal("Recent", topics[0].Topic);
        Assert.Equal(2, topics[0].Count);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoTrendAlertItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AnalyticsHighlight));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_SetsLatestAtToMostRecentCreatedAtPerTopic()
    {
        await using var context = CreateContext();
        var oldest = DateTimeOffset.UtcNow.AddDays(-5);
        var middle = DateTimeOffset.UtcNow.AddDays(-3);
        var newest = DateTimeOffset.UtcNow.AddDays(-1);
        context.FeedItems.Add(CreateTrendAlert("AI", createdAt: oldest));
        context.FeedItems.Add(CreateTrendAlert("AI", createdAt: middle));
        context.FeedItems.Add(CreateTrendAlert("AI", createdAt: newest));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var topics = result.Value!;
        Assert.Single(topics);
        Assert.Equal(3, topics[0].Count);
        Assert.Equal(newest, topics[0].LatestAt);
    }
}
