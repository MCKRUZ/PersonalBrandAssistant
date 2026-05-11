using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class CreateContentHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesContentWithProvidedFields()
    {
        await using var context = CreateContext();
        var handler = new CreateContent.Handler(context);

        var command = new CreateContent.Command(
            "Test Title", ContentType.BlogPost, Platform.Blog, null, ["tag1", "tag2"]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var content = await context.Contents.FindAsync(result.Value);
        Assert.NotNull(content);
        Assert.Equal("Test Title", content.Title);
        Assert.Equal(ContentType.BlogPost, content.ContentType);
        Assert.Equal(Platform.Blog, content.PrimaryPlatform);
        Assert.Equal(["tag1", "tag2"], content.Tags);
    }

    [Fact]
    public async Task Handle_DefaultsStatusToIdea()
    {
        await using var context = CreateContext();
        var handler = new CreateContent.Handler(context);

        var command = new CreateContent.Command(
            "Title", ContentType.Tweet, Platform.Twitter, null, []);

        var result = await handler.Handle(command, CancellationToken.None);

        var content = await context.Contents.FindAsync(result.Value);
        Assert.Equal(ContentStatus.Idea, content!.Status);
    }

    [Fact]
    public async Task Handle_WhenSourceIdeaIdProvided_CopiesIdeaTitleAndDescription()
    {
        await using var context = CreateContext();
        var idea = new Idea
        {
            Title = "Idea Title",
            Description = "Idea Description",
            Status = IdeaStatus.New
        };
        context.Ideas.Add(idea);
        await context.SaveChangesAsync();

        var handler = new CreateContent.Handler(context);
        var command = new CreateContent.Command(
            "", ContentType.BlogPost, Platform.Blog, idea.Id, []);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var content = await context.Contents.FindAsync(result.Value);
        Assert.Equal("Idea Title", content!.Title);
        Assert.Equal("Idea Description", content.Body);
    }

    [Fact]
    public async Task Handle_WhenSourceIdeaIdProvided_SetsIdeaStatusToUsed()
    {
        await using var context = CreateContext();
        var idea = new Idea
        {
            Title = "Idea",
            Description = "Description",
            Status = IdeaStatus.New
        };
        context.Ideas.Add(idea);
        await context.SaveChangesAsync();

        var handler = new CreateContent.Handler(context);
        var command = new CreateContent.Command(
            "", ContentType.BlogPost, Platform.Blog, idea.Id, []);

        await handler.Handle(command, CancellationToken.None);

        var reloaded = await context.Ideas.FindAsync(idea.Id);
        Assert.Equal(IdeaStatus.Used, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_ReturnsNewContentId()
    {
        await using var context = CreateContext();
        var handler = new CreateContent.Handler(context);

        var command = new CreateContent.Command(
            "Title", ContentType.LinkedInPost, Platform.LinkedIn, null, []);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }

    [Fact]
    public async Task Handle_WhenSourceIdeaIdNotFound_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var handler = new CreateContent.Handler(context);

        var command = new CreateContent.Command(
            "Title", ContentType.BlogPost, Platform.Blog, Guid.NewGuid(), []);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
