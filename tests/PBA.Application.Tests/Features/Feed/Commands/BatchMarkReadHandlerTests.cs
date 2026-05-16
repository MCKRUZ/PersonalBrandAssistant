using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class BatchMarkReadHandlerTests
{
    [Fact]
    public async Task Handle_MarksAllUnreadItemsAsRead_ReturnsCount()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 5; i++)
            context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification, isRead: false));
        for (var i = 0; i < 3; i++)
            context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification, isRead: true));
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
        Assert.True(await context.FeedItems.AllAsync(f => f.IsRead));
    }

    [Fact]
    public async Task Handle_FiltersByType_WhenSpecified()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification, isRead: false));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft, isRead: false));
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(
            new BatchMarkRead.Command(Type: FeedItemType.TrendAlert), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        var notification = await context.FeedItems.FirstAsync(f => f.Type == FeedItemType.SystemNotification);
        Assert.False(notification.IsRead);
        var draft = await context.FeedItems.FirstAsync(f => f.Type == FeedItemType.AgentDraft);
        Assert.False(draft.IsRead);
    }

    [Fact]
    public async Task Handle_FiltersByIds_WhenSpecified()
    {
        await using var context = CreateContext();
        var item1 = CreateFeedItem();
        var item2 = CreateFeedItem();
        var item3 = CreateFeedItem();
        context.FeedItems.AddRange(item1, item2, item3);
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(
            new BatchMarkRead.Command(Ids: [item1.Id, item2.Id]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        var updated3 = await context.FeedItems.FindAsync(item3.Id);
        Assert.False(updated3!.IsRead);
    }

    [Fact]
    public async Task Handle_SkipsExpiredItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.SystemNotification,
            isRead: false,
            expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.TrendAlert,
            isRead: false,
            expiresAt: DateTimeOffset.UtcNow.AddHours(-2)));
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task Handle_Returns0_WhenNoMatchingItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification, isRead: true));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert, isRead: true));
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}
