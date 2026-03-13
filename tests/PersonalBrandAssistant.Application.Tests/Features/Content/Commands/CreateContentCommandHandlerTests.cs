using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class CreateContentCommandHandlerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<DbSet<ContentEntity>> _contentsDbSet;
    private readonly CreateContentCommandHandler _handler;

    public CreateContentCommandHandlerTests()
    {
        _contentsDbSet = new List<ContentEntity>().AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(_contentsDbSet.Object);
        _handler = new CreateContentCommandHandler(_dbContext.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithId()
    {
        var command = new CreateContentCommand(ContentType.BlogPost, "Test body", "Test title");

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        _contentsDbSet.Verify(s => s.Add(It.IsAny<ContentEntity>()), Times.Once);
        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithMetadata_SetsMetadataOnContent()
    {
        var metadata = new ContentMetadata { Tags = ["tag1", "tag2"] };
        var command = new CreateContentCommand(ContentType.SocialPost, "Body", Metadata: metadata);

        ContentEntity? captured = null;
        _contentsDbSet.Setup(s => s.Add(It.IsAny<ContentEntity>()))
            .Callback<ContentEntity>(c => captured = c);

        await _handler.Handle(command, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(metadata, captured!.Metadata);
    }

    [Fact]
    public async Task Handle_WithTargetPlatforms_SetsTargetPlatforms()
    {
        var platforms = new[] { PlatformType.TwitterX, PlatformType.LinkedIn };
        var command = new CreateContentCommand(ContentType.BlogPost, "Body", TargetPlatforms: platforms);

        ContentEntity? captured = null;
        _contentsDbSet.Setup(s => s.Add(It.IsAny<ContentEntity>()))
            .Callback<ContentEntity>(c => captured = c);

        await _handler.Handle(command, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(platforms, captured!.TargetPlatforms);
    }

    [Fact]
    public async Task Handle_CallsSaveChanges()
    {
        var command = new CreateContentCommand(ContentType.BlogPost, "Body");

        await _handler.Handle(command, CancellationToken.None);

        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
