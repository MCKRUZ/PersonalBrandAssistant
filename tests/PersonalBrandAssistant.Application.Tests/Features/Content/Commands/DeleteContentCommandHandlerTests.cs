using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
using PersonalBrandAssistant.Domain.Enums;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class DeleteContentCommandHandlerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();

    private DeleteContentCommandHandler CreateHandler(List<ContentEntity> data)
    {
        var mockDbSet = data.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        return new DeleteContentCommandHandler(_dbContext.Object);
    }

    [Fact]
    public async Task Handle_ExistingContent_ArchivesSuccessfully()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
        var handler = CreateHandler([content]);

        var result = await handler.Handle(new DeleteContentCommand(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Archived, content.Status);
    }

    [Fact]
    public async Task Handle_ContentNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler([]);

        var result = await handler.Handle(new DeleteContentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_AlreadyArchived_ReturnsSuccessIdempotently()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
        content.TransitionTo(ContentStatus.Archived);
        var handler = CreateHandler([content]);

        var result = await handler.Handle(new DeleteContentCommand(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
