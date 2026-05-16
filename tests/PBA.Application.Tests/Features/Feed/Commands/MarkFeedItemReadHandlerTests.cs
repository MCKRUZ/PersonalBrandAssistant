using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class MarkFeedItemReadHandlerTests
{
    [Fact]
    public async Task Handle_MarksItemAsRead()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.SystemNotification, isRead: false);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var handler = new MarkFeedItemRead.Handler(context);
        var result = await handler.Handle(new MarkFeedItemRead.Command(item.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
    }

    [Fact]
    public async Task Handle_AlreadyReadItem_ReturnsSuccess()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.SystemNotification, isRead: true);
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
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
