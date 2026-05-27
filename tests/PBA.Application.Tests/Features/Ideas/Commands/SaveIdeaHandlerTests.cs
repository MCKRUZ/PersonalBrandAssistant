using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class SaveIdeaHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesNewSavedIdea_WhenNoExistingSavedDetails()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "key1", SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new SaveIdea.Handler(db);
        var result = await handler.Handle(
            new SaveIdea.Command { IdeaId = idea.Id, Notes = "Great idea", Tags = ["ai"] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SavedIdeas.FirstOrDefaultAsync(s => s.IdeaId == idea.Id);
        Assert.NotNull(saved);
        Assert.Equal("Great idea", saved.Notes);
        Assert.Contains("ai", saved.Tags);
    }

    [Fact]
    public async Task Handle_UpdatesExistingSavedIdea_WhenAlreadySaved()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "key2", SourceName = "test-source" };
        idea.SavedDetails = new SavedIdea { IdeaId = idea.Id, Notes = "Old notes" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new SaveIdea.Handler(db);
        var result = await handler.Handle(
            new SaveIdea.Command { IdeaId = idea.Id, Notes = "New notes", Tags = ["updated"] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SavedIdeas.FirstOrDefaultAsync(s => s.IdeaId == idea.Id);
        Assert.NotNull(saved);
        Assert.Equal("New notes", saved.Notes);
        Assert.Contains("updated", saved.Tags);
    }

    [Fact]
    public async Task Handle_SetsStatusToSaved()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "key3", Status = IdeaStatus.New, SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new SaveIdea.Handler(db);
        await handler.Handle(
            new SaveIdea.Command { IdeaId = idea.Id },
            CancellationToken.None);

        var updated = await db.Ideas.FindAsync(idea.Id);
        Assert.Equal(IdeaStatus.Saved, updated!.Status);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenIdeaDoesNotExist()
    {
        using var db = CreateContext();
        var handler = new SaveIdea.Handler(db);

        var result = await handler.Handle(
            new SaveIdea.Command { IdeaId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Domain.Common.ResultFailureType.NotFound, result.FailureType);
    }
}
