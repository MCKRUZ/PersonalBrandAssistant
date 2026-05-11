using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class DeleteContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_TransitionsToArchived()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new DeleteContent.Handler(context);
        var result = await handler.Handle(new DeleteContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Archived, reloaded!.Status);
        Assert.True(reloaded.IsDeleted);
    }

    [Fact]
    public async Task Handle_CascadesArchiveToUnpublishedChildren()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity { Title = "Parent", Status = ContentStatus.Draft };
        var draftChild = new ContentEntity
        {
            Title = "Draft Child",
            Status = ContentStatus.Draft,
            ParentContentId = parent.Id
        };
        var publishedChild = new ContentEntity
        {
            Title = "Published Child",
            Status = ContentStatus.Published,
            ParentContentId = parent.Id
        };
        context.Contents.AddRange(parent, draftChild, publishedChild);
        await context.SaveChangesAsync();

        var handler = new DeleteContent.Handler(context);
        await handler.Handle(new DeleteContent.Command(parent.Id), CancellationToken.None);

        var reloadedDraft = await context.Contents.FindAsync(draftChild.Id);
        Assert.Equal(ContentStatus.Archived, reloadedDraft!.Status);
        Assert.True(reloadedDraft.IsDeleted);
    }

    [Fact]
    public async Task Handle_DoesNotCascadeToPublishedChildren()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity { Title = "Parent", Status = ContentStatus.Draft };
        var publishedChild = new ContentEntity
        {
            Title = "Published Child",
            Status = ContentStatus.Published,
            ParentContentId = parent.Id
        };
        context.Contents.AddRange(parent, publishedChild);
        await context.SaveChangesAsync();

        var handler = new DeleteContent.Handler(context);
        await handler.Handle(new DeleteContent.Command(parent.Id), CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(publishedChild.Id);
        Assert.Equal(ContentStatus.Published, reloaded!.Status);
        Assert.False(reloaded.IsDeleted);
    }

    [Fact]
    public async Task Handle_ReturnsErrorForInvalidTransition()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Archived };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new DeleteContent.Handler(context);
        var result = await handler.Handle(new DeleteContent.Command(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ArchivesIdeaStatusContent()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Idea };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new DeleteContent.Handler(context);
        var result = await handler.Handle(new DeleteContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Archived, reloaded!.Status);
        Assert.True(reloaded.IsDeleted);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenContentDoesNotExist()
    {
        await using var context = CreateContext();

        var handler = new DeleteContent.Handler(context);
        var result = await handler.Handle(new DeleteContent.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
