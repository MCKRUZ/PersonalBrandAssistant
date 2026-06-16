using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.BrandRankingProfile.Queries;
using PBA.Domain.Common;
using PBA.Infrastructure.Data;
using BrandPillar = PBA.Domain.Entities.BrandPillar;
using BrandRankingProfileEntity = PBA.Domain.Entities.BrandRankingProfile;
using Xunit;

namespace PBA.Application.Tests.Features.BrandRankingProfileApi;

public class GetActiveBrandRankingProfileHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static BrandRankingProfileEntity ActiveProfile() => new()
    {
        Version = 3, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
        Positioning = "Pos", AudiencePrimary = "Aud", AudienceSecondary = "Sec",
        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
        AuthorityTopics = ["auth1"], AntiTopics = ["anti1"], VoiceMarkers = ["vm1"],
        Pillars =
        [
            new BrandPillar { Name = "Second", Description = "d2", Weight = 0.4, Order = 1 },
            new BrandPillar { Name = "First", Description = "d1", Weight = 0.6, Order = 0 },
        ]
    };

    [Fact]
    public async Task Handle_ActiveProfilePresent_ReturnsDtoWithPillarsOrdered()
    {
        await using var db = CreateContext();
        db.BrandRankingProfiles.Add(ActiveProfile());
        await db.SaveChangesAsync();

        var result = await new GetActiveBrandRankingProfile.Handler(db)
            .Handle(new GetActiveBrandRankingProfile.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(3, dto.Version);
        Assert.Equal(["First", "Second"], dto.Pillars.Select(p => p.Name)); // ordered by Order
        Assert.Equal(["auth1"], dto.AuthorityTopics);
        Assert.Equal(["anti1"], dto.AntiTopics);
        Assert.Equal(["vm1"], dto.VoiceMarkers);
    }

    [Fact]
    public async Task Handle_NoActiveProfile_ReturnsNotFound()
    {
        await using var db = CreateContext();

        var result = await new GetActiveBrandRankingProfile.Handler(db)
            .Handle(new GetActiveBrandRankingProfile.Query(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_ReturnsConcurrencyToken()
    {
        await using var db = CreateContext();
        db.BrandRankingProfiles.Add(ActiveProfile());
        await db.SaveChangesAsync();

        var result = await new GetActiveBrandRankingProfile.Handler(db)
            .Handle(new GetActiveBrandRankingProfile.Query(), CancellationToken.None);

        Assert.Equal("0", result.Value!.ConcurrencyToken); // xmin stringified (0 under InMemory)
    }
}
