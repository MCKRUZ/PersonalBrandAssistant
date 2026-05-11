using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class UpdateContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_UpdatesOnlyProvidedFields()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Original",
            Body = "Original body",
            Status = ContentStatus.Draft
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "Updated", null, null, null, null, content.UpdatedAt);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal("Updated", reloaded!.Title);
        Assert.Equal("Original body", reloaded.Body);
    }

    [Fact]
    public async Task Handle_SetsUpdatedAt()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();
        var originalUpdatedAt = content.UpdatedAt;

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "New Title", null, null, null, null, content.UpdatedAt);

        await handler.Handle(command, CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.True(reloaded!.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public async Task Handle_RejectsWhenLastUpdatedAtDoesNotMatch_OptimisticConcurrency()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var staleTimestamp = content.UpdatedAt.AddMinutes(-5);
        var command = new UpdateContent.Command(
            content.Id, "Stale Update", null, null, null, null, staleTimestamp);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_RejectsWhenStatusIsPublished()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Published };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "New", null, null, null, null, content.UpdatedAt);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_RejectsWhenStatusIsArchived()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Archived };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "New", null, null, null, null, content.UpdatedAt);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_AllowsSaveWhenStatusIsDraft()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "Updated", null, null, null, null, content.UpdatedAt);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_AllowsSaveWhenStatusIsIdea()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Idea };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "Updated", null, null, null, null, content.UpdatedAt);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_AllowsSaveWhenStatusIsReview()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Review };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            content.Id, "Updated", null, null, null, null, content.UpdatedAt);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenContentDoesNotExist()
    {
        await using var context = CreateContext();

        var handler = new UpdateContent.Handler(context);
        var command = new UpdateContent.Command(
            Guid.NewGuid(), "Title", null, null, null, null, DateTimeOffset.UtcNow);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
