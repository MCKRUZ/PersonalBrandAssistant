using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
using PersonalBrandAssistant.Domain.Enums;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;

public class UpdateContentCommandHandlerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();

    private UpdateContentCommandHandler CreateHandler(List<ContentEntity> data)
    {
        var mockDbSet = data.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        return new UpdateContentCommandHandler(_dbContext.Object);
    }

    [Fact]
    public async Task Handle_ExistingDraftContent_UpdatesSuccessfully()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Old body", "Old title");
        var handler = CreateHandler([content]);

        var command = new UpdateContentCommand(content.Id, Title: "New title", Body: "New body");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New title", content.Title);
        Assert.Equal("New body", content.Body);
    }

    [Fact]
    public async Task Handle_ContentNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler([]);

        var command = new UpdateContentCommand(Guid.NewGuid(), Title: "New title");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ContentNotEditable_ReturnsValidationFailed()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        var handler = CreateHandler([content]);

        var command = new UpdateContentCommand(content.Id, Body: "Updated");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReviewContent_IsEditable()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
        content.TransitionTo(ContentStatus.Review);
        var handler = CreateHandler([content]);

        var command = new UpdateContentCommand(content.Id, Body: "Updated in review");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated in review", content.Body);
    }

    [Fact]
    public async Task Handle_ConcurrencyConflict_ReturnsConflict()
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Body");
        var handler = CreateHandler([content]);
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var command = new UpdateContentCommand(content.Id, Body: "Updated");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
    }
}
