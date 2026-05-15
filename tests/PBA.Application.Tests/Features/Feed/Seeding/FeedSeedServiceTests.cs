using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Seeding;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Seeding;

public class FeedSeedServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_CreatesItemsOfAllFeedItemTypeValues()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        await service.SeedAsync();

        var types = await context.FeedItems.Select(f => f.Type).Distinct().ToListAsync();
        foreach (var expected in Enum.GetValues<FeedItemType>())
            Assert.Contains(expected, types);
    }

    [Fact]
    public async Task SeedAsync_CreatesMixOfReadAndUnreadItems()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        await service.SeedAsync();

        Assert.Contains(await context.FeedItems.ToListAsync(), f => f.IsRead);
        Assert.Contains(await context.FeedItems.ToListAsync(), f => !f.IsRead);
    }

    [Fact]
    public async Task SeedAsync_CreatesMixOfActedAndNotActedItems()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        await service.SeedAsync();

        Assert.Contains(await context.FeedItems.ToListAsync(), f => f.IsActedOn);
        Assert.Contains(await context.FeedItems.ToListAsync(), f => !f.IsActedOn);
    }

    [Fact]
    public async Task SeedAsync_CreatesItemsWithVaryingPriorities()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        await service.SeedAsync();

        var priorities = await context.FeedItems.Select(f => f.Priority).Distinct().ToListAsync();
        Assert.True(priorities.Count >= 3, $"Expected at least 3 priorities, got {priorities.Count}");
    }

    [Fact]
    public async Task SeedAsync_CreatesSomeExpiredItems()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        await service.SeedAsync();

        var expired = await context.FeedItems
            .Where(f => f.ExpiresAt != null && f.ExpiresAt < DateTimeOffset.UtcNow)
            .CountAsync();
        Assert.True(expired >= 1, "Expected at least 1 expired item");
    }

    [Fact]
    public async Task SeedAsync_CreatesItemsWithValidDataJson()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        await service.SeedAsync();

        var items = await context.FeedItems.ToListAsync();
        foreach (var item in items.Where(i => i.Data is not null))
        {
            var doc = JsonDocument.Parse(item.Data!);
            Assert.NotNull(doc);
        }

        var trendAlerts = items.Where(i => i.Type == FeedItemType.TrendAlert && i.Data is not null);
        foreach (var item in trendAlerts)
        {
            using var doc = JsonDocument.Parse(item.Data!);
            Assert.True(doc.RootElement.TryGetProperty("topic", out _), "TrendAlert missing 'topic' field");
        }

        var analytics = items.Where(i => i.Type == FeedItemType.AnalyticsHighlight && i.Data is not null);
        foreach (var item in analytics)
        {
            using var doc = JsonDocument.Parse(item.Data!);
            Assert.True(doc.RootElement.TryGetProperty("delta", out _), "AnalyticsHighlight missing 'delta' field");
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_ReturnsZeroOnSecondCall()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        var firstCount = await service.SeedAsync();
        var secondCount = await service.SeedAsync();

        Assert.True(firstCount > 0);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task SeedAsync_ReturnsCountOfCreatedItems()
    {
        await using var context = CreateContext();
        var service = new FeedSeedService(context);

        var count = await service.SeedAsync();

        Assert.True(count >= 30 && count <= 50, $"Expected 30-50 items, got {count}");
        Assert.Equal(count, await context.FeedItems.CountAsync());
    }
}
