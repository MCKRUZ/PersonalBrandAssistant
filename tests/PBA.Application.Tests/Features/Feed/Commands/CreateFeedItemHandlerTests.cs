using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Feed.Commands;
using PBA.Application.Features.Feed.Dtos;
using PBA.Domain.Enums;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class CreateFeedItemHandlerTests
{
    [Fact]
    public async Task Handle_CreatesFeedItemWithAllFields_AndSaves()
    {
        await using var context = CreateContext();
        var notifierMock = new Mock<IFeedNotifier>();
        var loggerMock = new Mock<ILogger<CreateFeedItem.Handler>>();
        var targetId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        var handler = new CreateFeedItem.Handler(context, notifierMock.Object, loggerMock.Object);
        var result = await handler.Handle(new CreateFeedItem.Command(
            Type: FeedItemType.TrendAlert,
            Title: "Trending: AI",
            Summary: "AI is trending across platforms",
            Data: @"{""topic"":""AI"",""source"":""Twitter""}",
            ActionType: "view",
            ActionTargetId: targetId,
            Priority: FeedItemPriority.High,
            ExpiresAt: expiresAt), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = await context.FeedItems.FindAsync(result.Value);
        Assert.NotNull(item);
        Assert.Equal(FeedItemType.TrendAlert, item!.Type);
        Assert.Equal("Trending: AI", item.Title);
        Assert.Equal("AI is trending across platforms", item.Summary);
        Assert.Equal(@"{""topic"":""AI"",""source"":""Twitter""}", item.Data);
        Assert.Equal("view", item.ActionType);
        Assert.Equal(targetId, item.ActionTargetId);
        Assert.Equal(FeedItemPriority.High, item.Priority);
        Assert.Equal(expiresAt, item.ExpiresAt);
        Assert.False(item.IsRead);
        Assert.False(item.IsActedOn);
    }

    [Fact]
    public async Task Handle_PushesNewItemViaFeedNotifier()
    {
        await using var context = CreateContext();
        var notifierMock = new Mock<IFeedNotifier>();
        var loggerMock = new Mock<ILogger<CreateFeedItem.Handler>>();

        var handler = new CreateFeedItem.Handler(context, notifierMock.Object, loggerMock.Object);
        await handler.Handle(new CreateFeedItem.Command(
            Type: FeedItemType.SystemNotification,
            Title: "Test",
            Summary: "Test summary",
            Data: null,
            ActionType: null,
            ActionTargetId: null), CancellationToken.None);

        notifierMock.Verify(n => n.NotifyNewItemAsync(It.IsAny<FeedItemDto>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ContinuesSuccessfully_EvenIfNotifierFails()
    {
        await using var context = CreateContext();
        var notifierMock = new Mock<IFeedNotifier>();
        notifierMock.Setup(n => n.NotifyNewItemAsync(It.IsAny<FeedItemDto>()))
            .ThrowsAsync(new InvalidOperationException("SignalR down"));
        var loggerMock = new Mock<ILogger<CreateFeedItem.Handler>>();

        var handler = new CreateFeedItem.Handler(context, notifierMock.Object, loggerMock.Object);
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

    [Fact]
    public async Task Handle_LogsError_WhenNotifierFails()
    {
        await using var context = CreateContext();
        var notifierMock = new Mock<IFeedNotifier>();
        notifierMock.Setup(n => n.NotifyNewItemAsync(It.IsAny<FeedItemDto>()))
            .ThrowsAsync(new InvalidOperationException("SignalR down"));
        var loggerMock = new Mock<ILogger<CreateFeedItem.Handler>>();

        var handler = new CreateFeedItem.Handler(context, notifierMock.Object, loggerMock.Object);
        await handler.Handle(new CreateFeedItem.Command(
            Type: FeedItemType.SystemNotification,
            Title: "Test",
            Summary: "Summary",
            Data: null,
            ActionType: null,
            ActionTargetId: null), CancellationToken.None);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
