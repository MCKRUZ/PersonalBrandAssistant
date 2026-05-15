using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Queries;

public class GetTrendingTopicsHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FeedItem CreateTrendAlert(string topic, DateTimeOffset? createdAt = null)
    {
        return new FeedItem
        {
            Title = $"Trend: {topic}",
            Type = FeedItemType.TrendAlert,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Data = $@"{{""topic"":""{topic}"",""source"":""Twitter"",""mentionCount"":10,""sentiment"":""positive""}}"
        };
    }

    [Fact]
    public async Task Handle_ReturnsTopicsGroupedAndCounted()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateTrendAlert("AI"));
        context.FeedItems.Add(CreateTrendAlert("AI"));
        context.FeedItems.Add(CreateTrendAlert("AI"));
        context.FeedItems.Add(CreateTrendAlert("Rust"));
        context.FeedItems.Add(CreateTrendAlert("Rust"));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("AI", result.Value[0].Topic);
        Assert.Equal(3, result.Value[0].Count);
        Assert.Equal("Rust", result.Value[1].Topic);
        Assert.Equal(2, result.Value[1].Count);
    }

    [Fact]
    public async Task Handle_OrdersByCountDescending()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateTrendAlert("Less Popular"));
        context.FeedItems.Add(CreateTrendAlert("Most Popular"));
        context.FeedItems.Add(CreateTrendAlert("Most Popular"));
        context.FeedItems.Add(CreateTrendAlert("Most Popular"));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Most Popular", result.Value![0].Topic);
        Assert.Equal(3, result.Value[0].Count);
    }

    [Fact]
    public async Task Handle_LimitsToTop10()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 12; i++)
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
    public async Task Handle_ReturnsEmptyListWhenNoTrendAlertItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem
        {
            Title = "Not a trend",
            Type = FeedItemType.SystemNotification,
        });
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
        var oldest = DateTimeOffset.UtcNow.AddDays(-3);
        var newest = DateTimeOffset.UtcNow.AddDays(-1);
        context.FeedItems.Add(CreateTrendAlert("AI", createdAt: oldest));
        context.FeedItems.Add(CreateTrendAlert("AI", createdAt: newest));
        await context.SaveChangesAsync();

        var handler = new GetTrendingTopics.Handler(context);
        var result = await handler.Handle(new GetTrendingTopics.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var topics = result.Value!;
        Assert.Single(topics);
        Assert.Equal(newest, topics[0].LatestAt);
    }
}
