using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class CreateFeedItemHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesItemWithAllFieldsAndSaves()
    {
        await using var context = CreateContext();
        var notifier = new Mock<IFeedNotifier>();
        var logger = new Mock<ILogger<CreateFeedItem.Handler>>();

        var handler = new CreateFeedItem.Handler(context, notifier.Object, logger.Object);
        var result = await handler.Handle(new CreateFeedItem.Command(
            Type: FeedItemType.TrendAlert,
            Title: "Trending: AI",
            Summary: "AI is trending",
            Data: @"{""topic"":""AI""}",
            ActionType: null,
            ActionTargetId: null,
            Priority: FeedItemPriority.High), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = await context.FeedItems.FindAsync(result.Value);
        Assert.NotNull(item);
        Assert.Equal("Trending: AI", item!.Title);
        Assert.Equal(FeedItemType.TrendAlert, item.Type);
        Assert.Equal(FeedItemPriority.High, item.Priority);
    }

    [Fact]
    public async Task Handle_PushesNewItemViaNotifier()
    {
        await using var context = CreateContext();
        var notifier = new Mock<IFeedNotifier>();
        var logger = new Mock<ILogger<CreateFeedItem.Handler>>();

        var handler = new CreateFeedItem.Handler(context, notifier.Object, logger.Object);
        await handler.Handle(new CreateFeedItem.Command(
            Type: FeedItemType.SystemNotification,
            Title: "Test",
            Summary: "Test summary",
            Data: null,
            ActionType: null,
            ActionTargetId: null), CancellationToken.None);

        notifier.Verify(n => n.NotifyNewItemAsync(It.IsAny<Application.Features.Feed.Dtos.FeedItemDto>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ContinuesSuccessfullyEvenIfNotifierFails()
    {
        await using var context = CreateContext();
        var notifier = new Mock<IFeedNotifier>();
        notifier.Setup(n => n.NotifyNewItemAsync(It.IsAny<Application.Features.Feed.Dtos.FeedItemDto>()))
            .ThrowsAsync(new InvalidOperationException("SignalR down"));
        var logger = new Mock<ILogger<CreateFeedItem.Handler>>();

        var handler = new CreateFeedItem.Handler(context, notifier.Object, logger.Object);
        var result = await handler.Handle(new CreateFeedItem.Command(
            Type: FeedItemType.SystemNotification,
            Title: "Test",
            Summary: "Summary",
            Data: null,
            ActionType: null,
            ActionTargetId: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.FeedItems);
    }
}
