using MediatR;
using Moq;
using PBA.Application.Features.Feed.Commands;
using PBA.Domain.Common;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class BatchActHandlerTests
{
    [Fact]
    public async Task Handle_ProcessesMultipleItems_ReturnsSuccessCount()
    {
        var knownIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(
                It.Is<ActOnFeedItem.Command>(c => knownIds.Contains(c.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.Success(
                new ActOnFeedItem.ActOnFeedItemResponse(true)));

        var handler = new BatchAct.Handler(senderMock.Object);
        var result = await handler.Handle(new BatchAct.Command(knownIds, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.SuccessCount);
        Assert.Empty(result.Value.Failures);
    }

    [Fact]
    public async Task Handle_HandlesPartialFailures_WithFailureDetails()
    {
        var knownIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var unknownId = Guid.NewGuid();
        var allIds = new List<Guid>(knownIds) { unknownId };

        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(
                It.Is<ActOnFeedItem.Command>(c => knownIds.Contains(c.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.Success(
                new ActOnFeedItem.ActOnFeedItemResponse(true)));
        senderMock.Setup(s => s.Send(
                It.Is<ActOnFeedItem.Command>(c => !knownIds.Contains(c.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActOnFeedItem.ActOnFeedItemResponse>.NotFound("Not found"));

        var handler = new BatchAct.Handler(senderMock.Object);
        var result = await handler.Handle(new BatchAct.Command(allIds, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.SuccessCount);
        Assert.Single(result.Value.Failures);
        Assert.Equal(unknownId, result.Value.Failures[0].Id);
        Assert.NotEmpty(result.Value.Failures[0].Reason);
    }
}
