using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class DismissIdeaHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_SetsStatusToDismissed()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "key1", Status = IdeaStatus.New };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new DismissIdea.Handler(db);
        var result = await handler.Handle(
            new DismissIdea.Command(idea.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await db.Ideas.FindAsync(idea.Id);
        Assert.Equal(IdeaStatus.Dismissed, updated!.Status);
    }

    [Fact]
    public async Task Handle_RemovesSavedDetails_WhenTheyExist()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "key2", Status = IdeaStatus.Saved };
        idea.SavedDetails = new SavedIdea { IdeaId = idea.Id, Notes = "Keep this" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new DismissIdea.Handler(db);
        await handler.Handle(new DismissIdea.Command(idea.Id), CancellationToken.None);

        Assert.Empty(await db.SavedIdeas.ToListAsync());
    }

    [Fact]
    public async Task Handle_Succeeds_WhenNoSavedDetails()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "key3", Status = IdeaStatus.New };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new DismissIdea.Handler(db);
        var result = await handler.Handle(
            new DismissIdea.Command(idea.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenIdeaDoesNotExist()
    {
        using var db = CreateContext();
        var handler = new DismissIdea.Handler(db);

        var result = await handler.Handle(
            new DismissIdea.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
