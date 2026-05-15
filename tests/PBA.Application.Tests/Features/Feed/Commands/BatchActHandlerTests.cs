using MediatR;
using Moq;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Common;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class BatchActHandlerTests
{
    [Fact]
    public async Task Handle_ProcessesMultipleItemsAndReturnsSuccessCount()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ActOnFeedItem.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.Success(new ActOnFeedItem.ActOnFeedItemResponse(true)));

        var handler = new BatchAct.Handler(sender.Object);
        var result = await handler.Handle(new BatchAct.Command(ids, "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.SuccessCount);
        Assert.Empty(result.Value.Failures);
    }

    [Fact]
    public async Task Handle_HandlesPartialFailuresGracefully()
    {
        var successId = Guid.NewGuid();
        var failId = Guid.NewGuid();
        var sender = new Mock<ISender>();

        sender.Setup(s => s.Send(It.Is<ActOnFeedItem.Command>(c => c.Id == successId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.Success(new ActOnFeedItem.ActOnFeedItemResponse(true)));

        sender.Setup(s => s.Send(It.Is<ActOnFeedItem.Command>(c => c.Id == failId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.NotFound("Not found"));

        var handler = new BatchAct.Handler(sender.Object);
        var result = await handler.Handle(new BatchAct.Command([successId, failId], "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.SuccessCount);
        Assert.Single(result.Value.Failures);
        Assert.Equal(failId, result.Value.Failures[0].Id);
    }

    [Fact]
    public async Task Handle_ReturnsFailureDetailsForFailedItems()
    {
        var failId = Guid.NewGuid();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ActOnFeedItem.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.ValidationFailure(["Unknown action"]));

        var handler = new BatchAct.Handler(sender.Object);
        var result = await handler.Handle(new BatchAct.Command([failId], "bad-action"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.SuccessCount);
        Assert.Single(result.Value.Failures);
        Assert.Contains("Unknown action", result.Value.Failures[0].Reason);
    }
}
