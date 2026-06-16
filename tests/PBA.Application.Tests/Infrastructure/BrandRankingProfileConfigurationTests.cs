using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.FeedRanking;

// InMemory-level mapping checks (jsonb lists + owned pillar relationship round-trip). The vector(1536)
// column, partial unique index, and xmin concurrency are Postgres-only and covered by section-12's
// Testcontainers run — InMemory cannot model them.
public class BrandRankingProfileConfigurationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Profile_RoundTrips_PillarsAndStringLists()
    {
        var id = Guid.NewGuid();
        var pillarId = Guid.NewGuid();

        await using var db = NewContext();
        db.BrandRankingProfiles.Add(new BrandRankingProfile
        {
            Id = id,
            Version = 1,
            IsActive = true,
            Positioning = "pos",
            AudiencePrimary = "aud",
            AuthorityTopics = ["a1", "a2"],
            AntiTopics = ["x1"],
            VoiceMarkers = ["v1", "v2", "v3"],
            Pillars =
            [
                new BrandPillar { Id = pillarId, Name = "P1", Description = "d1", Weight = 0.6, Order = 0 },
                new BrandPillar { Name = "P2", Description = "d2", Weight = 0.4, Order = 1 },
            ],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // force a fresh load from the store

        var loaded = await db.BrandRankingProfiles.Include(p => p.Pillars).SingleAsync(p => p.Id == id);

        Assert.Equal(["a1", "a2"], loaded.AuthorityTopics);
        Assert.Equal(["x1"], loaded.AntiTopics);
        Assert.Equal(3, loaded.VoiceMarkers.Count);
        Assert.Equal(2, loaded.Pillars.Count);
        var p1 = loaded.Pillars.Single(p => p.Id == pillarId);
        Assert.Equal("P1", p1.Name);
        Assert.Equal(0.6, p1.Weight);
        Assert.Equal(id, p1.BrandRankingProfileId);
    }
}
