using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Queries;

public class ListIdeaSourcesHandlerTests
{
    private static ApplicationDbContext CreateContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_MultipleSources_ReturnsAllOrderedByName()
    {
        await using var context = CreateContext();
        context.IdeaSources.Add(new IdeaSource { Name = "Zeta Feed", Type = IdeaSourceType.RSS, IsEnabled = true });
        context.IdeaSources.Add(new IdeaSource { Name = "Alpha Feed", Type = IdeaSourceType.API, IsEnabled = true });
        context.IdeaSources.Add(new IdeaSource { Name = "Beta Feed", Type = IdeaSourceType.Manual, IsEnabled = false });
        await context.SaveChangesAsync();

        var handler = new ListIdeaSources.Handler(context);
        var result = await handler.Handle(new ListIdeaSources.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("Alpha Feed", result.Value[0].Name);
        Assert.Equal("Beta Feed", result.Value[1].Name);
        Assert.Equal("Zeta Feed", result.Value[2].Name);
    }

    [Fact]
    public async Task Handle_EnabledSourceWithNoFailures_IsHealthyTrue()
    {
        await using var context = CreateContext();
        context.IdeaSources.Add(new IdeaSource
        {
            Name = "Healthy Source",
            IsEnabled = true,
            ConsecutiveFailures = 0,
            LastSuccessAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var handler = new ListIdeaSources.Handler(context);
        var result = await handler.Handle(new ListIdeaSources.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value![0].IsHealthy);
    }

    [Fact]
    public async Task Handle_SourceWithThreeOrMoreFailures_IsHealthyFalse()
    {
        await using var context = CreateContext();
        context.IdeaSources.Add(new IdeaSource
        {
            Name = "Failing Source",
            IsEnabled = true,
            ConsecutiveFailures = 3,
            LastError = "Connection timeout"
        });
        await context.SaveChangesAsync();

        var handler = new ListIdeaSources.Handler(context);
        var result = await handler.Handle(new ListIdeaSources.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value![0].IsHealthy);
        Assert.Equal(3, result.Value[0].ConsecutiveFailures);
    }

    [Fact]
    public async Task Handle_SourceWithIdeas_IncludesIdeaCount()
    {
        var dbName = Guid.NewGuid().ToString();

        // Seed data in one context
        await using (var seedContext = CreateContext(dbName))
        {
            var source = new IdeaSource { Name = "Popular Source", IsEnabled = true };
            seedContext.IdeaSources.Add(source);
            await seedContext.SaveChangesAsync();

            for (var i = 0; i < 5; i++)
            {
                seedContext.Ideas.Add(new Idea
                {
                    Title = $"Idea {i}",
                    IdeaSourceId = source.Id,
                    DeduplicationKey = $"dedup-{i}"
                });
            }
            await seedContext.SaveChangesAsync();
        }

        // Query from a fresh context to avoid change tracker fixup on the IReadOnlyList navigation
        await using var queryContext = CreateContext(dbName);
        var handler = new ListIdeaSources.Handler(queryContext);
        var result = await handler.Handle(new ListIdeaSources.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value![0].IdeaCount);
    }
}
