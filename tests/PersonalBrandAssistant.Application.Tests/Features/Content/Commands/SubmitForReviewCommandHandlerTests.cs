using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Features.Content.Commands.SubmitForReview;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class SubmitForReviewCommandHandlerTests
{
    private readonly Mock<IContentPipeline> _pipeline = new();

    [Fact]
    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
    {
        var contentId = Guid.NewGuid();
        _pipeline.Setup(p => p.SubmitForReviewAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var handler = new SubmitForReviewCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new SubmitForReviewCommand(contentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _pipeline.Verify(p => p.SubmitForReviewAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ContentAlreadySubmitted_ReturnsConflict()
    {
        _pipeline.Setup(p => p.SubmitForReviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Conflict("Already in review"));

        var handler = new SubmitForReviewCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new SubmitForReviewCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
    }
}
