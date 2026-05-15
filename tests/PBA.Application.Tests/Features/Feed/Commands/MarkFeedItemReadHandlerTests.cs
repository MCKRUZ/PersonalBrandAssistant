using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class MarkFeedItemReadHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_MarksItemAsRead()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Test", Type = FeedItemType.SystemNotification, IsRead = false };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var handler = new MarkFeedItemRead.Handler(context);
        var result = await handler.Handle(new MarkFeedItemRead.Command(item.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
    }

    [Fact]
    public async Task Handle_AlreadyRead_ReturnsSuccess()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Test", Type = FeedItemType.SystemNotification, IsRead = true };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var handler = new MarkFeedItemRead.Handler(context);
        var result = await handler.Handle(new MarkFeedItemRead.Command(item.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NonexistentId_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new MarkFeedItemRead.Handler(context);
        var result = await handler.Handle(new MarkFeedItemRead.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Domain.Common.ResultFailureType.NotFound, result.FailureType);
    }
}
