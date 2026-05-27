using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class CreateContentFromIdeaTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesContentFromIdea()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "AI Governance Trends", Description = "Some analysis", DeduplicationKey = "k1", SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new CreateContentFromIdea.Handler(db);
        var result = await handler.Handle(
            new CreateContentFromIdea.Command(idea.Id, ContentType.Blog, Platform.LinkedIn),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var content = await db.Contents.FirstOrDefaultAsync(c => c.SourceIdeaId == idea.Id);
        Assert.NotNull(content);
        Assert.Equal("AI Governance Trends", content.Title);
        Assert.Equal("Some analysis", content.Body);
        Assert.Equal(ContentType.Blog, content.ContentType);
        Assert.Equal(Platform.LinkedIn, content.PrimaryPlatform);
        Assert.Equal(idea.Id, content.SourceIdeaId);
    }

    [Fact]
    public async Task Handle_SetsBodyToEmptyString_WhenDescriptionIsNull()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "No Description", Description = null, DeduplicationKey = "k2", SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new CreateContentFromIdea.Handler(db);
        await handler.Handle(
            new CreateContentFromIdea.Command(idea.Id, ContentType.Tweet, Platform.Twitter),
            CancellationToken.None);

        var content = await db.Contents.FirstOrDefaultAsync(c => c.SourceIdeaId == idea.Id);
        Assert.NotNull(content);
        Assert.Equal(string.Empty, content.Body);
    }

    [Fact]
    public async Task Handle_SetsContentStatusToIdea()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "k3", SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new CreateContentFromIdea.Handler(db);
        await handler.Handle(
            new CreateContentFromIdea.Command(idea.Id, ContentType.LinkedInPost, Platform.LinkedIn),
            CancellationToken.None);

        var content = await db.Contents.FirstOrDefaultAsync(c => c.SourceIdeaId == idea.Id);
        Assert.Equal(ContentStatus.Idea, content!.Status);
    }

    [Fact]
    public async Task Handle_SetsIdeaStatusToUsed()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "k4", Status = IdeaStatus.Saved, SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new CreateContentFromIdea.Handler(db);
        await handler.Handle(
            new CreateContentFromIdea.Command(idea.Id, ContentType.Blog, Platform.Blog),
            CancellationToken.None);

        var updated = await db.Ideas.FindAsync(idea.Id);
        Assert.Equal(IdeaStatus.Used, updated!.Status);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenIdeaDoesNotExist()
    {
        using var db = CreateContext();
        var handler = new CreateContentFromIdea.Handler(db);

        var result = await handler.Handle(
            new CreateContentFromIdea.Command(Guid.NewGuid(), ContentType.Blog, Platform.Blog),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_ReturnsNewContentId()
    {
        using var db = CreateContext();
        var idea = new Idea { Title = "Test", DeduplicationKey = "k5", SourceName = "test-source" };
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var handler = new CreateContentFromIdea.Handler(db);
        var result = await handler.Handle(
            new CreateContentFromIdea.Command(idea.Id, ContentType.Blog, Platform.Blog),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);

        var content = await db.Contents.FindAsync(result.Value);
        Assert.NotNull(content);
    }
}
