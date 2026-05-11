using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Queries;

public class GetIdeaHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_ExistingIdea_ReturnsDetailDto()
    {
        await using var context = CreateContext();
        var source = new IdeaSource { Name = "RSS Feed", Type = IdeaSourceType.RSS, FeedUrl = "https://example.com/feed" };
        context.IdeaSources.Add(source);

        var idea = new Idea
        {
            Title = "Test Idea",
            Description = "Full description",
            Url = "https://example.com/article",
            SourceName = "Example",
            IdeaSourceId = source.Id,
            Category = "AI",
            Summary = "Short summary",
            Status = IdeaStatus.New,
            DeduplicationKey = "test-key"
        };
        context.Ideas.Add(idea);
        await context.SaveChangesAsync();

        var handler = new GetIdea.Handler(context);
        var result = await handler.Handle(new GetIdea.Query(idea.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal("Test Idea", dto.Title);
        Assert.Equal("Full description", dto.Description);
        Assert.Equal("https://example.com/article", dto.Url);
        Assert.Equal("AI", dto.Category);
        Assert.NotNull(dto.SourceInfo);
        Assert.Equal("RSS Feed", dto.SourceInfo!.Name);
        Assert.False(dto.HasSavedDetails);
        Assert.Null(dto.SavedDetails);
    }

    [Fact]
    public async Task Handle_NonExistentIdea_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new GetIdea.Handler(context);
        var result = await handler.Handle(new GetIdea.Query(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_IdeaWithAIConnections_ParsesJsonCorrectly()
    {
        await using var context = CreateContext();
        var connections = new List<IdeaConnectionDto>
        {
            new()
            {
                Theme = "AI Trends",
                RelatedIdeaIds = [Guid.NewGuid()],
                SuggestedAngle = "Compare approaches",
                Confidence = 0.85
            }
        };

        var idea = new Idea
        {
            Title = "Connected Idea",
            AIConnections = JsonSerializer.Serialize(connections),
            DeduplicationKey = "connected-key"
        };
        context.Ideas.Add(idea);
        await context.SaveChangesAsync();

        var handler = new GetIdea.Handler(context);
        var result = await handler.Handle(new GetIdea.Query(idea.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.AIConnections);
        Assert.Single(result.Value.AIConnections!);
        Assert.Equal("AI Trends", result.Value.AIConnections![0].Theme);
        Assert.Equal(0.85, result.Value.AIConnections[0].Confidence);
    }

    [Fact]
    public async Task Handle_IdeaWithNoSavedDetails_ReturnsNullSavedDetails()
    {
        await using var context = CreateContext();
        var idea = new Idea
        {
            Title = "Unsaved Idea",
            Status = IdeaStatus.New,
            DeduplicationKey = "unsaved-key"
        };
        context.Ideas.Add(idea);
        await context.SaveChangesAsync();

        var handler = new GetIdea.Handler(context);
        var result = await handler.Handle(new GetIdea.Query(idea.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SavedDetails);
        Assert.False(result.Value.HasSavedDetails);
    }
}
