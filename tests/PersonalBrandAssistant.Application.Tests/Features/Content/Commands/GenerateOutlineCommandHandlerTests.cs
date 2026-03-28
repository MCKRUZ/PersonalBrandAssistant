using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Features.Content.Commands.GenerateOutline;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class GenerateOutlineCommandHandlerTests
{
    private readonly Mock<IContentPipeline> _pipeline = new();

    [Fact]
    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
    {
        var contentId = Guid.NewGuid();
        _pipeline.Setup(p => p.GenerateOutlineAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("1. Intro\n2. Body"));

        var handler = new GenerateOutlineCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new GenerateOutlineCommand(contentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _pipeline.Verify(p => p.GenerateOutlineAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ContentNotFound_ReturnsNotFound()
    {
        _pipeline.Setup(p => p.GenerateOutlineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.NotFound("Not found"));

        var handler = new GenerateOutlineCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new GenerateOutlineCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }
}
