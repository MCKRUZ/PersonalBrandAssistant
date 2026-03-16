using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Features.Content.Commands.ValidateVoice;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class ValidateVoiceCommandHandlerTests
{
    private readonly Mock<IContentPipeline> _pipeline = new();

    [Fact]
    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
    {
        var contentId = Guid.NewGuid();
        var score = new BrandVoiceScore(90, 85, 88, 92, [], []);
        _pipeline.Setup(p => p.ValidateVoiceAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(score));

        var handler = new ValidateVoiceCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new ValidateVoiceCommand(contentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(90, result.Value!.OverallScore);
        _pipeline.Verify(p => p.ValidateVoiceAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ContentNotFound_ReturnsNotFound()
    {
        _pipeline.Setup(p => p.ValidateVoiceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.NotFound("Not found"));

        var handler = new ValidateVoiceCommandHandler(_pipeline.Object);
        var result = await handler.Handle(new ValidateVoiceCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }
}
