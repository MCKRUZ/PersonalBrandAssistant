using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class CreateFromTopicCommandHandlerTests
{
    private readonly Mock<IContentPipeline> _pipeline = new();

    [Fact]
    public async Task Handle_ValidCommand_DelegatesToContentPipeline()
    {
        var contentId = Guid.NewGuid();
        _pipeline.Setup(p => p.CreateFromTopicAsync(It.IsAny<ContentCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(contentId));

        var handler = new CreateFromTopicCommandHandler(_pipeline.Object);
        var command = new CreateFromTopicCommand(ContentType.BlogPost, "AI trends");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(contentId, result.Value);
        _pipeline.Verify(p => p.CreateFromTopicAsync(
            It.Is<ContentCreationRequest>(r => r.Topic == "AI trends" && r.Type == ContentType.BlogPost),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PipelineReturnsFailure_ReturnsFailure()
    {
        _pipeline.Setup(p => p.CreateFromTopicAsync(It.IsAny<ContentCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure(ErrorCode.ValidationFailed, "Topic is empty"));

        var handler = new CreateFromTopicCommandHandler(_pipeline.Object);
        var command = new CreateFromTopicCommand(ContentType.BlogPost, "");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }
}
