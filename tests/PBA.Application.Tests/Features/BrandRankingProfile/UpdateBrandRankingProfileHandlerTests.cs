using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.BrandRankingProfile.Commands;
using PBA.Domain.Common;
using PBA.Infrastructure.Data;
using BrandPillar = PBA.Domain.Entities.BrandPillar;
using BrandRankingProfileEntity = PBA.Domain.Entities.BrandRankingProfile;
using Xunit;

namespace PBA.Application.Tests.Features.BrandRankingProfileApi;

public class UpdateBrandRankingProfileHandlerTests
{
    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ApplicationDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    // SaveChangesAsync always throws a concurrency exception — to exercise the handler's catch->Conflict
    // path, which InMemory cannot trigger via the real xmin token.
    private sealed class ConcurrencyThrowingContext(DbContextOptions<ApplicationDbContext> o)
        : ApplicationDbContext(o)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new DbUpdateConcurrencyException("stale token");
    }

    private static BrandRankingProfileEntity Profile(int version = 1) => new()
    {
        Version = version, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
        Positioning = "Pos", AudiencePrimary = "Aud", AudienceSecondary = "Sec",
        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
        AuthorityTopics = ["auth1"], AntiTopics = ["anti1"], VoiceMarkers = ["vm1"],
        Pillars = [new BrandPillar { Id = P1, Name = "P1", Description = "D1", Weight = 0.6, Order = 0,
            DescriptionEmbedding = new float[] { 1, 0 } }]
    };

    // A command that mirrors the profile (no definition change) unless mutated by the caller.
    private static UpdateBrandRankingProfile.Command CommandFrom(
        BrandRankingProfileEntity p,
        IReadOnlyList<UpdateBrandRankingProfile.PillarInput>? pillars = null,
        string? positioning = null,
        IReadOnlyList<string>? authorityTopics = null,
        string token = "0") => new()
    {
        Positioning = positioning ?? p.Positioning,
        AudiencePrimary = p.AudiencePrimary,
        AudienceSecondary = p.AudienceSecondary,
        HalfLifeDays = p.HalfLifeDays, DecayFloor = p.DecayFloor,
        AntiTopicMultiplier = p.AntiTopicMultiplier, AuthorityBoost = p.AuthorityBoost,
        Pillars = pillars ?? p.Pillars
            .Select(pl => new UpdateBrandRankingProfile.PillarInput(pl.Id, pl.Name, pl.Description, pl.Weight, pl.Order))
            .ToList(),
        AuthorityTopics = authorityTopics ?? p.AuthorityTopics.ToList(),
        AntiTopics = p.AntiTopics.ToList(),
        VoiceMarkers = p.VoiceMarkers.ToList(),
        ConcurrencyToken = token
    };

    [Fact]
    public async Task Handle_WeightsOnly_AppliesWeightsAndKnobs_NoVersionBump()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        db.BrandRankingProfiles.Add(Profile());
        await db.SaveChangesAsync();

        var cmd = CommandFrom(Profile(),
            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1", "D1", 0.9, 0)]) // weight 0.6 -> 0.9
            with { HalfLifeDays = 14 };

        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = db.BrandRankingProfiles.Include(p => p.Pillars).Single();
        Assert.Equal(1, saved.Version);          // no bump
        Assert.Equal(0.9, saved.Pillars.Single().Weight);
        Assert.Equal(14, saved.HalfLifeDays);
    }

    [Fact]
    public async Task Handle_WeightsOnly_DoesNotAlterPillarNameOrDescription()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        db.BrandRankingProfiles.Add(Profile());
        await db.SaveChangesAsync();

        // Same definition (name/description) but different weight -> weights-only path.
        var cmd = CommandFrom(Profile(),
            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1", "D1", 0.3, 0)]);

        await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);

        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
        Assert.Equal("P1", pillar.Name);
        Assert.Equal("D1", pillar.Description);
        Assert.NotNull(pillar.DescriptionEmbedding); // untouched on the weights path
    }

    [Fact]
    public async Task Handle_TopicChange_BumpsVersion()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        db.BrandRankingProfiles.Add(Profile(version: 5));
        await db.SaveChangesAsync();

        var cmd = CommandFrom(Profile(version: 5), authorityTopics: ["auth1", "NEW"]);

        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value!.Version);
        Assert.Equal(["auth1", "NEW"], db.BrandRankingProfiles.Single().AuthorityTopics);
    }

    [Fact]
    public async Task Handle_PillarDescriptionChange_BumpsVersion_AndNullsEmbedding()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        db.BrandRankingProfiles.Add(Profile());
        await db.SaveChangesAsync();

        var cmd = CommandFrom(Profile(),
            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1", "D1-CHANGED", 0.6, 0)]);

        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);

        Assert.Equal(2, result.Value!.Version);
        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
        Assert.Equal("D1-CHANGED", pillar.Description);
        Assert.Null(pillar.DescriptionEmbedding); // nulled so section-06 re-embeds
    }

    [Fact]
    public async Task Handle_NameChangeMixedWithWeightChange_StillBumps_NoSmuggling()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        db.BrandRankingProfiles.Add(Profile());
        await db.SaveChangesAsync();

        // A definition change (rename) cannot be slipped through as a non-bumping weights update (R-H4).
        var cmd = CommandFrom(Profile(),
            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1-RENAMED", "D1", 0.9, 0)]);

        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);

        Assert.Equal(2, result.Value!.Version);
        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
        Assert.Equal("P1-RENAMED", pillar.Name);
        Assert.Equal(0.9, pillar.Weight);
    }

    [Fact]
    public async Task Handle_NoActiveProfile_ReturnsNotFound()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        var result = await new UpdateBrandRankingProfile.Handler(db)
            .Handle(CommandFrom(Profile()), CancellationToken.None);

        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_MissingConcurrencyToken_FailsClosed_NoLostUpdate()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        db.BrandRankingProfiles.Add(Profile());
        await db.SaveChangesAsync();

        // Empty token must be REJECTED, not silently allowed to skip the concurrency check (R-H3).
        var cmd = CommandFrom(Profile(), token: "") with { HalfLifeDays = 99 };

        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);

        Assert.Equal(ResultFailureType.Validation, result.FailureType);
        Assert.Equal(7, db.BrandRankingProfiles.Single().HalfLifeDays); // no mutation persisted
    }

    [Fact]
    public async Task Handle_ConcurrencyConflict_ReturnsConflict()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(name))
        {
            seed.BrandRankingProfiles.Add(Profile());
            await seed.SaveChangesAsync();
        }

        await using var throwing = new ConcurrencyThrowingContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

        var result = await new UpdateBrandRankingProfile.Handler(throwing)
            .Handle(CommandFrom(Profile()), CancellationToken.None);

        Assert.Equal(ResultFailureType.Conflict, result.FailureType);
    }
}
