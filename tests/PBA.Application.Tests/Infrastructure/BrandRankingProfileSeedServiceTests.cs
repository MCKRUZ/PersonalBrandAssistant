using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Seeding;
using Xunit;

namespace PBA.Application.Tests.FeedRanking;

public class BrandRankingProfileSeedServiceTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SeedAsync_FirstRun_InsertsV1ActiveProfileWithFivePillars()
    {
        await using var db = NewContext();
        var sut = new BrandRankingProfileSeedService(db);

        var count = await sut.SeedAsync();

        Assert.Equal(1, count);
        var profile = await db.BrandRankingProfiles.Include(p => p.Pillars).SingleAsync();
        Assert.True(profile.IsActive);
        Assert.Equal(1, profile.Version);
        Assert.Equal(5, profile.Pillars.Count);
        // v0 weights sum to 1.0
        Assert.Equal(1.0, profile.Pillars.Sum(p => p.Weight), precision: 6);
        Assert.Contains(profile.Pillars, p => p.Name == "Agent-Native Architecture" && p.Weight == 0.28);
        Assert.NotEmpty(profile.AuthorityTopics);
        Assert.NotEmpty(profile.AntiTopics);
        Assert.NotEmpty(profile.VoiceMarkers);
        Assert.Equal(7, profile.HalfLifeDays);
        Assert.Equal(0.1, profile.AntiTopicMultiplier);
        Assert.Equal(1.2, profile.AuthorityBoost);
    }

    [Fact]
    public async Task SeedAsync_SecondRun_IsNoOp()
    {
        await using var db = NewContext();
        var sut = new BrandRankingProfileSeedService(db);
        await sut.SeedAsync();
        var first = await db.BrandRankingProfiles.SingleAsync();

        var secondCount = await sut.SeedAsync();

        Assert.Equal(0, secondCount);
        var all = await db.BrandRankingProfiles.ToListAsync();
        Assert.Single(all);
        Assert.Equal(first.Id, all[0].Id);
    }

    [Fact]
    public async Task SeedAsync_WhenActiveProfileAlreadyExists_DoesNotInsert()
    {
        await using var db = NewContext();
        db.BrandRankingProfiles.Add(new BrandRankingProfile
        {
            Version = 1,
            IsActive = true,
            Positioning = "pre-existing",
            AudiencePrimary = "pre-existing",
        });
        await db.SaveChangesAsync();
        var sut = new BrandRankingProfileSeedService(db);

        var count = await sut.SeedAsync();

        Assert.Equal(0, count);
        Assert.Single(await db.BrandRankingProfiles.ToListAsync());
    }
}
