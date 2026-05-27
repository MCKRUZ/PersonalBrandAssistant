using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class DeleteIdeaSourceHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_RemovesSource()
    {
        using var db = CreateContext();
        var source = new IdeaSource { Name = "To Delete", Category = "Test" };
        db.IdeaSources.Add(source);
        await db.SaveChangesAsync();

        var handler = new DeleteIdeaSource.Handler(db);
        var result = await handler.Handle(
            new DeleteIdeaSource.Command(source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await db.IdeaSources.FindAsync(source.Id));
    }

    [Fact]
    public async Task Handle_SetsNullOnChildIdeas()
    {
        using var db = CreateContext();
        var source = new IdeaSource { Name = "Parent Source", Category = "Test" };
        db.IdeaSources.Add(source);

        var idea = new Idea
        {
            Title = "Child Idea",
            DeduplicationKey = "child1",
            IdeaSourceId = source.Id,
            SourceName = "test-source"
        };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new DeleteIdeaSource.Handler(db);
        await handler.Handle(new DeleteIdeaSource.Command(source.Id), CancellationToken.None);

        var updatedIdea = await db.Ideas.FindAsync(idea.Id);
        Assert.NotNull(updatedIdea);
        Assert.Null(updatedIdea.IdeaSourceId);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenSourceDoesNotExist()
    {
        using var db = CreateContext();
        var handler = new DeleteIdeaSource.Handler(db);

        var result = await handler.Handle(
            new DeleteIdeaSource.Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
