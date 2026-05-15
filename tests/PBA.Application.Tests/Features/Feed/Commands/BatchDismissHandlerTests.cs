using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class BatchDismissHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_DismissesAllItemsOfSpecifiedType()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Alert 1", Type = FeedItemType.TrendAlert });
        context.FeedItems.Add(new FeedItem { Title = "Alert 2", Type = FeedItemType.TrendAlert });
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.TrendAlert), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.True(await context.FeedItems.Where(f => f.Type == FeedItemType.TrendAlert).AllAsync(f => f.IsRead && f.IsActedOn));
    }

    [Fact]
    public async Task Handle_SkipsExpiredItems()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Active", Type = FeedItemType.TrendAlert });
        context.FeedItems.Add(new FeedItem { Title = "Expired", Type = FeedItemType.TrendAlert, ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.TrendAlert), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public async Task Handle_DoesNotAffectOtherTypes()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Alert", Type = FeedItemType.TrendAlert });
        context.FeedItems.Add(new FeedItem { Title = "Notif", Type = FeedItemType.SystemNotification });
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        await handler.Handle(new BatchDismiss.Command(FeedItemType.TrendAlert), CancellationToken.None);

        var notif = await context.FeedItems.FirstAsync(f => f.Type == FeedItemType.SystemNotification);
        Assert.False(notif.IsRead);
        Assert.False(notif.IsActedOn);
    }

    [Fact]
    public async Task Handle_SkipsAlreadyDismissed()
    {
        await using var context = CreateContext();
        context.FeedItems.Add(new FeedItem { Title = "Already dismissed", Type = FeedItemType.TrendAlert, IsActedOn = true });
        context.FeedItems.Add(new FeedItem { Title = "New", Type = FeedItemType.TrendAlert });
        await context.SaveChangesAsync();

        var handler = new BatchDismiss.Handler(context);
        var result = await handler.Handle(new BatchDismiss.Command(FeedItemType.TrendAlert), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }
}
