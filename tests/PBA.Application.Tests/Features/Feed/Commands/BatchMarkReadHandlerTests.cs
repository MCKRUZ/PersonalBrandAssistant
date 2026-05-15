using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class BatchMarkReadHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_MarksAllUnreadItemsAsRead()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 3; i++)
            context.FeedItems.Add(new FeedItem { Title = $"Unread {i}", Type = FeedItemType.SystemNotification, IsRead = false });
        context.FeedItems.Add(new FeedItem { Title = "Already Read", Type = FeedItemType.SystemNotification, IsRead = true });
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.True(await context.FeedItems.AllAsync(f => f.IsRead));
    }

    [Fact]
    public async Task Handle_FiltersByTypeWhenSpecified()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Alert", Type = FeedItemType.TrendAlert, IsRead = false });
        context.FeedItems.Add(new FeedItem { Title = "Alert 2", Type = FeedItemType.TrendAlert, IsRead = false });
        context.FeedItems.Add(new FeedItem { Title = "Notif", Type = FeedItemType.SystemNotification, IsRead = false });
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(Type: FeedItemType.TrendAlert), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        var notif = await context.FeedItems.FirstAsync(f => f.Type == FeedItemType.SystemNotification);
        Assert.False(notif.IsRead);
    }

    [Fact]
    public async Task Handle_SkipsExpiredItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Active", Type = FeedItemType.SystemNotification, IsRead = false });
        context.FeedItems.Add(new FeedItem { Title = "Expired", Type = FeedItemType.SystemNotification, IsRead = false, ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public async Task Handle_NoMatchingItems_ReturnsZero()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Already Read", Type = FeedItemType.SystemNotification, IsRead = true });
        await context.SaveChangesAsync();

        var handler = new BatchMarkRead.Handler(context);
        var result = await handler.Handle(new BatchMarkRead.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}
