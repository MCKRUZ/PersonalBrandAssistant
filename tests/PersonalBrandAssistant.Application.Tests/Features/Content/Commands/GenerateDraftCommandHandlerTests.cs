using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Features.Content.Commands.GenerateDraft;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class GenerateDraftCommandHandlerTests
{
    private readonly Mock<IContentPipeline> _pipeline = new();

    [Fact]
    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
    {
        var contentId = Guid.NewGuid();
        _pipeline.Setup(p => p.GenerateDraftAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("Draft content here"));

        var handler = new GenerateDraftCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new GenerateDraftCommand(contentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _pipeline.Verify(p => p.GenerateDraftAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ContentNotFound_ReturnsNotFound()
    {
        _pipeline.Setup(p => p.GenerateDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.NotFound("Not found"));

        var handler = new GenerateDraftCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new GenerateDraftCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }
}
