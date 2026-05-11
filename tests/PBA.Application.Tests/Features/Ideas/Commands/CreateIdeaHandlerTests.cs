using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class CreateIdeaHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesIdea_WithStatusNew_SourceNameManual()
    {
        using var db = CreateContext();
        var handler = new CreateIdea.Handler(db);

        var result = await handler.Handle(
            new CreateIdea.Command { Title = "Test Idea", Tags = ["dotnet"] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var idea = await db.Ideas.FindAsync(result.Value);
        Assert.NotNull(idea);
        Assert.Equal(IdeaStatus.New, idea.Status);
        Assert.Equal("Manual", idea.SourceName);
    }

    [Fact]
    public async Task Handle_GeneratesDeduplicationKey_FromNormalizedUrl()
    {
        using var db = CreateContext();
        var handler = new CreateIdea.Handler(db);

        var result = await handler.Handle(
            new CreateIdea.Command
            {
                Title = "Test",
                Url = "https://Example.COM/article?utm_source=twitter&valid=1"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var idea = await db.Ideas.FindAsync(result.Value);
        Assert.NotNull(idea);
        Assert.NotEmpty(idea.DeduplicationKey);

        // Same URL with different utm params should produce the same key
        var key1 = CreateIdea.GenerateDeduplicationKey(
            "https://Example.COM/article?utm_source=twitter&valid=1", "Test");
        var key2 = CreateIdea.GenerateDeduplicationKey(
            "https://example.com/article?utm_medium=email&valid=1", "Test");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public async Task Handle_GeneratesDeduplicationKey_FromTitle_WhenNoUrl()
    {
        using var db = CreateContext();
        var handler = new CreateIdea.Handler(db);

        var result = await handler.Handle(
            new CreateIdea.Command { Title = "My Great Idea" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var idea = await db.Ideas.FindAsync(result.Value);
        Assert.NotNull(idea);

        var expectedKey = CreateIdea.GenerateDeduplicationKey(null, "My Great Idea");
        Assert.Equal(expectedKey, idea.DeduplicationKey);
    }

    [Fact]
    public async Task Handle_ReturnsDuplicateError_WhenDeduplicationKeyExists()
    {
        using var db = CreateContext();
        var handler = new CreateIdea.Handler(db);

        var key = CreateIdea.GenerateDeduplicationKey(null, "Duplicate Idea");
        db.Ideas.Add(new Idea { Title = "Duplicate Idea", DeduplicationKey = key });
        await db.SaveChangesAsync();

        var result = await handler.Handle(
            new CreateIdea.Command { Title = "Duplicate Idea" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Errors.First());
    }

    [Fact]
    public async Task Handle_SetsDetectedAt_ToUtcNow()
    {
        using var db = CreateContext();
        var handler = new CreateIdea.Handler(db);
        var before = DateTimeOffset.UtcNow;

        var result = await handler.Handle(
            new CreateIdea.Command { Title = "Timed Idea" },
            CancellationToken.None);

        var idea = await db.Ideas.FindAsync(result.Value);
        Assert.NotNull(idea);
        Assert.True(idea.DetectedAt >= before);
        Assert.True(idea.DetectedAt <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Handle_ReturnsNewId()
    {
        using var db = CreateContext();
        var handler = new CreateIdea.Handler(db);

        var result = await handler.Handle(
            new CreateIdea.Command { Title = "New Idea" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }
}
