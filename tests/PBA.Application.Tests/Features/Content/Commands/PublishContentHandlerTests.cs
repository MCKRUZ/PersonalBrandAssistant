using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Commands;

public class PublishContentHandlerTests
{
    private readonly Mock<IContentPublisher> _publisher = new();

    private PublishContent.Handler CreateHandler() => new(_publisher.Object);

    [Fact]
    public async Task Handle_WithTargetPlatforms_PassesPlatformsToPublisher()
    {
        var contentId = Guid.NewGuid();
        var platforms = new List<Platform> { Platform.Blog, Platform.Medium }.AsReadOnly();
        var publishResult = new PublishResult(true, "https://example.com/post", []);
        _publisher.Setup(p => p.PublishAsync(contentId, platforms, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishResult);

        var handler = CreateHandler();
        var result = await handler.Handle(new PublishContent.Command(contentId, platforms), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _publisher.Verify(p => p.PublishAsync(contentId, platforms, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutTargetPlatforms_PassesNullToPublisher()
    {
        var contentId = Guid.NewGuid();
        var publishResult = new PublishResult(true, "https://example.com/post", []);
        _publisher.Setup(p => p.PublishAsync(contentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishResult);

        var handler = CreateHandler();
        var result = await handler.Handle(new PublishContent.Command(contentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _publisher.Verify(p => p.PublishAsync(contentId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PublisherFails_ReturnsFailure()
    {
        var contentId = Guid.NewGuid();
        var publishResult = new PublishResult(false, null, []);
        _publisher.Setup(p => p.PublishAsync(contentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishResult);

        var handler = CreateHandler();
        var result = await handler.Handle(new PublishContent.Command(contentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
