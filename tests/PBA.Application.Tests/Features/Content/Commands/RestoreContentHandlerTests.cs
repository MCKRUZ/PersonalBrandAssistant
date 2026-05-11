using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class RestoreContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_FromArchived_TransitionsToDraft()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Archived };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new RestoreContent.Handler(context);
        var result = await handler.Handle(new RestoreContent.Command(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.Contents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == content.Id);
        Assert.Equal(ContentStatus.Draft, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_SetsIsDeletedToFalse()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Test",
            Status = ContentStatus.Archived,
            IsDeleted = true
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new RestoreContent.Handler(context);
        await handler.Handle(new RestoreContent.Command(content.Id), CancellationToken.None);

        var reloaded = await context.Contents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == content.Id);
        Assert.False(reloaded!.IsDeleted);
    }

    [Fact]
    public async Task Handle_FromDraft_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Status = ContentStatus.Draft };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var handler = new RestoreContent.Handler(context);
        var result = await handler.Handle(new RestoreContent.Command(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NonexistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new RestoreContent.Handler(context);
        var result = await handler.Handle(new RestoreContent.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
