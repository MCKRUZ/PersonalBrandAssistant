using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Seeding;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas;

public class IdeaSourceSeedServiceTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SeedAsync_OnEmptyDatabase_CreatesRssSources()
    {
        await using var context = CreateContext();
        var service = new IdeaSourceSeedService(context);

        var count = await service.SeedAsync();
        var dbCount = await context.IdeaSources.CountAsync();

        Assert.True(count > 0);
        Assert.Equal(dbCount, count);
    }

    [Fact]
    public async Task SeedAsync_CreatesAllSourcesAsEnabledRssFeeds()
    {
        await using var context = CreateContext();
        var service = new IdeaSourceSeedService(context);

        await service.SeedAsync();

        var sources = await context.IdeaSources.ToListAsync();
        Assert.All(sources, s =>
        {
            Assert.Equal(IdeaSourceType.RSS, s.Type);
            Assert.True(s.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(s.FeedUrl));
            Assert.False(string.IsNullOrWhiteSpace(s.Category));
        });
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_ReturnsZeroOnSecondCall()
    {
        await using var context = CreateContext();
        var service = new IdeaSourceSeedService(context);

        var firstCount = await service.SeedAsync();
        var secondCount = await service.SeedAsync();

        Assert.True(firstCount > 0);
        Assert.Equal(0, secondCount);
        Assert.Equal(firstCount, await context.IdeaSources.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_DoesNotDuplicateExistingFeedUrls()
    {
        await using var context = CreateContext();
        context.IdeaSources.Add(new IdeaSource
        {
            Name = "Existing TLDR AI",
            Type = IdeaSourceType.RSS,
            FeedUrl = "https://tldr.tech/api/rss/ai",
            Category = "AI/ML",
        });
        await context.SaveChangesAsync();

        var service = new IdeaSourceSeedService(context);
        await service.SeedAsync();

        var aiFeeds = await context.IdeaSources
            .Where(s => s.FeedUrl == "https://tldr.tech/api/rss/ai")
            .CountAsync();
        Assert.Equal(1, aiFeeds);
    }

    [Fact]
    public async Task SeedAsync_DedupeIsCaseInsensitiveOnFeedUrl()
    {
        await using var context = CreateContext();
        context.IdeaSources.Add(new IdeaSource
        {
            Name = "Existing Dev.to (upper)",
            Type = IdeaSourceType.RSS,
            FeedUrl = "HTTPS://DEV.TO/FEED",
            Category = "General Dev",
        });
        await context.SaveChangesAsync();

        var service = new IdeaSourceSeedService(context);
        await service.SeedAsync();

        var devFeeds = await context.IdeaSources
            .Where(s => s.FeedUrl != null && s.FeedUrl.ToLower() == "https://dev.to/feed")
            .CountAsync();
        Assert.Equal(1, devFeeds);
    }
}
