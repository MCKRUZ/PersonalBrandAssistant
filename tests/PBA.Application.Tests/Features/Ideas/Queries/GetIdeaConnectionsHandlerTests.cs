using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Queries;

public class GetIdeaConnectionsHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_SavedIdeasWithConnections_ReturnsAllConnections()
    {
        await using var context = CreateContext();
        var connections1 = new List<IdeaConnectionDto>
        {
            new() { Theme = "AI", RelatedIdeaIds = [Guid.NewGuid()], SuggestedAngle = "Angle 1", Confidence = 0.9 }
        };
        var connections2 = new List<IdeaConnectionDto>
        {
            new() { Theme = "ML", RelatedIdeaIds = [Guid.NewGuid()], SuggestedAngle = "Angle 2", Confidence = 0.7 }
        };

        context.Ideas.Add(new Idea
        {
            Title = "Idea 1",
            Status = IdeaStatus.Saved,
            AIConnections = JsonSerializer.Serialize(connections1),
            DeduplicationKey = "key-1",
            SourceName = "test-source"
        });
        context.Ideas.Add(new Idea
        {
            Title = "Idea 2",
            Status = IdeaStatus.Saved,
            AIConnections = JsonSerializer.Serialize(connections2),
            DeduplicationKey = "key-2",
            SourceName = "test-source"
        });
        await context.SaveChangesAsync();

        var handler = new GetIdeaConnections.Handler(context);
        var result = await handler.Handle(new GetIdeaConnections.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, c => c.Theme == "AI");
        Assert.Contains(result.Value, c => c.Theme == "ML");
    }

    [Fact]
    public async Task Handle_NoSavedIdeas_ReturnsEmptyList()
    {
        await using var context = CreateContext();
        context.Ideas.Add(new Idea
        {
            Title = "New Idea",
            Status = IdeaStatus.New,
            AIConnections = "[{\"Theme\":\"Test\"}]",
            DeduplicationKey = "key-new",
            SourceName = "test-source"
        });
        await context.SaveChangesAsync();

        var handler = new GetIdeaConnections.Handler(context);
        var result = await handler.Handle(new GetIdeaConnections.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_MalformedJson_SkipsWithoutError()
    {
        await using var context = CreateContext();
        var validConnections = new List<IdeaConnectionDto>
        {
            new() { Theme = "Valid", RelatedIdeaIds = [], SuggestedAngle = "Good", Confidence = 0.5 }
        };

        context.Ideas.Add(new Idea
        {
            Title = "Valid",
            Status = IdeaStatus.Saved,
            AIConnections = JsonSerializer.Serialize(validConnections),
            DeduplicationKey = "key-valid",
            SourceName = "test-source"
        });
        context.Ideas.Add(new Idea
        {
            Title = "Broken",
            Status = IdeaStatus.Saved,
            AIConnections = "not valid json {{{",
            DeduplicationKey = "key-broken",
            SourceName = "test-source"
        });
        await context.SaveChangesAsync();

        var handler = new GetIdeaConnections.Handler(context);
        var result = await handler.Handle(new GetIdeaConnections.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!;
        Assert.Single(items);
        Assert.Equal("Valid", items[0].Theme);
    }

    [Fact]
    public async Task Handle_OnlyLoadsSavedIdeas_IgnoresOtherStatuses()
    {
        await using var context = CreateContext();
        var connections = JsonSerializer.Serialize(new List<IdeaConnectionDto>
        {
            new() { Theme = "Saved Theme", RelatedIdeaIds = [], SuggestedAngle = "Angle", Confidence = 0.8 }
        });

        context.Ideas.Add(new Idea { Title = "Saved", Status = IdeaStatus.Saved, AIConnections = connections, DeduplicationKey = "k1", SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "New", Status = IdeaStatus.New, AIConnections = connections, DeduplicationKey = "k2", SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Used", Status = IdeaStatus.Used, AIConnections = connections, DeduplicationKey = "k3", SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Dismissed", Status = IdeaStatus.Dismissed, AIConnections = connections, DeduplicationKey = "k4", SourceName = "test-source" });
        await context.SaveChangesAsync();

        var handler = new GetIdeaConnections.Handler(context);
        var result = await handler.Handle(new GetIdeaConnections.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }
}
