using PBA.Application.Common.Models;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Entities;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Queries;

public class ComputeRankTests
{
    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BrandRankingProfileSnapshot Profile(
        double w1 = 0.6, double w2 = 0.4, int version = 2,
        double halfLife = 7, double floor = 0.075, double antiMult = 0.1, double authBoost = 1.2) => new(
        Version: version,
        Positioning: "P", AudiencePrimary: "A", AudienceSecondary: null,
        Pillars:
        [
            new BrandPillarSnapshot(P1, "Pillar One", "d1", w1, 0, null),
            new BrandPillarSnapshot(P2, "Pillar Two", "d2", w2, 1, null),
        ],
        AuthorityTopics: [], AntiTopics: [], VoiceMarkers: [],
        HalfLifeDays: halfLife, DecayFloor: floor, AntiTopicMultiplier: antiMult, AuthorityBoost: authBoost);

    private static Dictionary<Guid, PillarSubScore> Subs(params (Guid id, double score)[] subs) =>
        subs.ToDictionary(s => s.id, s => new PillarSubScore
        {
            PillarId = s.id, PillarName = "n", Score = s.score, Reason = "r"
        });

    [Fact]
    public void ComputeRank_WeightedSumOverActivePillars_GivesRenormalizedBrandFit()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(
            Subs((P1, 0.8), (P2, 0.2)), null, null, 2, now, Profile(), now);

        // (0.6*0.8 + 0.4*0.2) / (0.6+0.4) = 0.56; age 0 -> recency 1; no flags -> rank == brandFit
        Assert.Equal(0.56, r.BrandFit, 6);
        Assert.Equal(0.56, r.Rank, 6);
    }

    [Fact]
    public void ComputeRank_OnlySomePillarsScored_RenormalizesOverPresentPillars()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(Subs((P1, 0.8)), null, null, 2, now, Profile(), now);

        // only P1 present: (0.6*0.8)/0.6 = 0.8 — scale stays comparable (R-C2b/c)
        Assert.Equal(0.8, r.BrandFit, 6);
        Assert.Single(r.Breakdown);
        Assert.Equal("Pillar One", r.Breakdown[0].Name);
    }

    [Theory]
    [InlineData(0, 1.0)]      // age 0 -> 1.0
    [InlineData(7, 0.5)]      // age == half-life -> 0.5
    [InlineData(14, 0.25)]    // two half-lives -> 0.25
    [InlineData(1000, 0.075)] // very old -> floor
    public void ComputeRank_Recency_FollowsHalfLifeWithFloor(double ageDays, double expected)
    {
        var now = DateTimeOffset.UtcNow;
        var detectedAt = now.AddDays(-ageDays);
        var r = ListIdeas.ComputeRank(
            Subs((P1, 1.0), (P2, 1.0)), null, null, 2, detectedAt, Profile(), now);

        Assert.Equal(expected, r.RecencyFactor, 3);
    }

    [Fact]
    public void ComputeRank_AntiTopicFlag_AppliesMultiplier()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(Subs((P1, 1.0), (P2, 1.0)), isAntiTopic: true, null, 2, now, Profile(), now);

        Assert.Equal(0.1, r.Rank, 6); // brandFit 1 × recency 1 × antiMult 0.1
    }

    [Fact]
    public void ComputeRank_NullAntiTopicFlag_NoMultiplier()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(Subs((P1, 1.0), (P2, 1.0)), isAntiTopic: null, null, 2, now, Profile(), now);

        Assert.Equal(1.0, r.Rank, 6);
    }

    [Fact]
    public void ComputeRank_AuthorityFlag_AppliesBoost()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(Subs((P1, 1.0), (P2, 1.0)), null, isAuthorityTopic: true, 2, now, Profile(), now);

        Assert.Equal(1.2, r.Rank, 6); // brandFit 1 × recency 1 × authBoost 1.2
    }

    [Fact]
    public void ComputeRank_BrandFitClampedToOne()
    {
        var now = DateTimeOffset.UtcNow;
        // A malformed sub-score above 1 renormalizes above 1; must clamp (R-M6).
        var r = ListIdeas.ComputeRank(Subs((P1, 2.0)), null, null, 2, now, Profile(), now);

        Assert.Equal(1.0, r.BrandFit, 6);
    }

    [Fact]
    public void ComputeRank_Unscored_RanksZero()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(Subs(), null, null, 2, now, Profile(), now);

        Assert.Equal(0, r.BrandFit, 6);
        Assert.Equal(0, r.Rank, 6);
        Assert.Empty(r.Breakdown);
    }

    [Fact]
    public void ComputeRank_OlderProfileVersion_IsStale()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = Profile(version: 3);

        Assert.True(ListIdeas.ComputeRank(Subs((P1, 0.5)), null, null, 2, now, profile, now).Stale);
        Assert.False(ListIdeas.ComputeRank(Subs((P1, 0.5)), null, null, 3, now, profile, now).Stale);
        Assert.False(ListIdeas.ComputeRank(Subs((P1, 0.5)), null, null, null, now, profile, now).Stale);
    }

    [Fact]
    public void ComputeRank_BreakdownUsesActivePillarNameAndStoredScore()
    {
        var now = DateTimeOffset.UtcNow;
        var r = ListIdeas.ComputeRank(Subs((P1, 0.75), (P2, 0.25)), null, null, 2, now, Profile(), now);

        var p1 = r.Breakdown.Single(b => b.Name == "Pillar One");
        Assert.Equal(0.75, p1.Score, 6);
        Assert.Equal("r", p1.Reason);
    }
}
