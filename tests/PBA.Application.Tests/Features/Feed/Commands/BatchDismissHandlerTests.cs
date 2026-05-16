using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class BatchDismissHandlerTests
{
    [Fact]
    public async Task Handle_DismissesAllItemsOfSpecifiedType()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.AgentDraft), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var drafts = await context.FeedItems.Where(f => f.Type == FeedItemType.AgentDraft).ToListAsync();
        Assert.All(drafts, d =>
        {
            Assert.True(d.IsRead);
            Assert.True(d.IsActedOn);
        });
    }

    [Fact]
    public async Task Handle_ReturnsCountOfDismissedItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.AgentDraft), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
    }

    [Fact]
    public async Task Handle_SkipsExpiredItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AgentDraft,
            expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));
        context.FeedItems.Add(CreateFeedItem(
            type: FeedItemType.AgentDraft,
            expiresAt: DateTimeOffset.UtcNow.AddHours(-2)));
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.AgentDraft), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task Handle_DoesNotAffectOtherTypes()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.AgentDraft));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.TrendAlert));
        context.FeedItems.Add(CreateFeedItem(type: FeedItemType.SystemNotification));
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        await handler.Handle(new BatchDismiss.Command(FeedItemType.AgentDraft), CancellationToken.None);

        var trendAlert = await context.FeedItems.FirstAsync(f => f.Type == FeedItemType.TrendAlert);
        Assert.False(trendAlert.IsRead);
        Assert.False(trendAlert.IsActedOn);

        var notification = await context.FeedItems.FirstAsync(f => f.Type == FeedItemType.SystemNotification);
        Assert.False(notification.IsRead);
        Assert.False(notification.IsActedOn);
    }

    [Fact]
    public async Task Handle_SkipsAlreadyDismissedItems()
    {
        await using var context = CreateContext();
        var alreadyDismissed = CreateFeedItem(type: FeedItemType.AgentDraft, isRead: true, isActedOn: true);
        var notDismissed = CreateFeedItem(type: FeedItemType.AgentDraft);
        context.FeedItems.AddRange(alreadyDismissed, notDismissed);
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.AgentDraft), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }
}
