using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaScoringServiceTests
{
    private static readonly Guid P1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid P2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // CurrentValue read-counter, to prove the sweep snapshots ranking options exactly once (R-L2).
    private sealed class CountingMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public int Reads { get; private set; }
        public T CurrentValue { get { Reads++; return value; } }
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static BrandRankingProfile Profile(int version = 2) => new()
    {
        Version = version, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
        Positioning = "P", AudiencePrimary = "A",
        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
        Pillars =
        [
            new BrandPillar { Id = P1, Name = "A", Description = "a", Weight = 0.6, Order = 0,
                DescriptionEmbedding = new float[] { 1, 0 } },
            new BrandPillar { Id = P2, Name = "B", Description = "b", Weight = 0.4, Order = 1,
                DescriptionEmbedding = new float[] { 0, 1 } },
        ]
    };

    private static Idea NewIdea(float[]? embedding, DateTimeOffset? detectedAt = null,
        int? scoredVersion = null, int attempts = 0) => new()
    {
        Title = "T", Description = "D", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
        Embedding = embedding, ScoredProfileVersion = scoredVersion, ScoreAttempts = attempts
    };

    private static (IdeaScoringService svc, ApplicationDbContext db, Mock<IIdeaAnalyzer> analyzer) Build(
        IdeaScoringOptions options, RankingOptions ranking, IOptionsMonitor<RankingOptions>? rankingMonitor = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var analyzer = new Mock<IIdeaAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(
                It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdeaAnalysis(
                [new PillarScore(P1, "A", 0.8, "r"), new PillarScore(P2, "B", 0.2, "r")],
                IsAntiTopic: false, IsAuthorityTopic: true, Reason: "reason"));

        // Embedder is real (shares the in-memory db) but its sidecar returns nothing, so EmbedPendingAsync
        // never silently embeds a test idea — candidate sets stay determined by the seeded Embedding values.
        var embedSidecar = new Mock<ISidecarClient>();
        embedSidecar.Setup(s => s.EmbedAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<float[]>)new List<float[]>());
        var embedder = new IdeaEmbeddingService(db, embedSidecar.Object,
            Mock.Of<IOptionsMonitor<EmbeddingOptions>>(m => m.CurrentValue == new EmbeddingOptions()),
            NullLogger<IdeaEmbeddingService>.Instance);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        sp.Setup(p => p.GetService(typeof(IIdeaAnalyzer))).Returns(analyzer.Object);
        sp.Setup(p => p.GetService(typeof(IdeaEmbeddingService))).Returns(embedder);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var monitor = rankingMonitor ?? Mock.Of<IOptionsMonitor<RankingOptions>>(m => m.CurrentValue == ranking);
        var svc = new IdeaScoringService(scopeFactory.Object, Options.Create(options), monitor,
            NullLogger<IdeaScoringService>.Instance);
        return (svc, db, analyzer);
    }

    private static void VerifyAnalyzed(Mock<IIdeaAnalyzer> analyzer, Times times) =>
        analyzer.Verify(a => a.AnalyzeAsync(
            It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()),
            times);

    private static RankingOptions Ranking() => new() { PreFilterThreshold = 0.5, ScoringWindowDays = 30 };

    [Fact]
    public async Task ScoreSweepAsync_CandidateSet_ExcludesOutOfWindowUnembeddedAndCurrentVersion()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile(version: 2));
        var valid = NewIdea(new float[] { 1, 0 });
        var old = NewIdea(new float[] { 1, 0 }, detectedAt: DateTimeOffset.UtcNow.AddDays(-40));
        var unembedded = NewIdea(null);
        var current = NewIdea(new float[] { 1, 0 }, scoredVersion: 2);
        db.Ideas.AddRange(valid, old, unembedded, current);
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        VerifyAnalyzed(analyzer, Times.Once());
        Assert.Equal(2, db.Ideas.Single(i => i.Id == valid.Id).ScoredProfileVersion);
        Assert.Null(db.Ideas.Single(i => i.Id == old.Id).ScoredProfileVersion);
        Assert.Null(db.Ideas.Single(i => i.Id == unembedded.Id).ScoredProfileVersion);
    }

    [Fact]
    public async Task ScoreSweepAsync_SnapshotsRankingOptionsOnce()
    {
        var counting = new CountingMonitor<RankingOptions>(Ranking());
        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true },
            new RankingOptions(), rankingMonitor: counting);
        db.BrandRankingProfiles.Add(Profile());
        db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        Assert.Equal(1, counting.Reads); // R-L2: snapshot once per sweep
    }

    [Fact]
    public async Task ScoreSweepAsync_AboveThreshold_StoresSubScoresFlagsVersionAndDerivedScore()
    {
        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile(version: 2));
        db.Ideas.Add(NewIdea(new float[] { 1, 0 })); // fit = 0.6 >= 0.5
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        var idea = db.Ideas.Single();
        Assert.Equal(2, idea.PillarSubScores.Count);
        Assert.Contains(idea.PillarSubScores, s => s.PillarId == P1 && Math.Abs(s.Score - 0.8) < 1e-9);
        Assert.True(idea.IsAuthorityTopic);
        Assert.False(idea.IsAntiTopic);
        Assert.Equal("reason", idea.ScoreReason);
        Assert.Equal(2, idea.ScoredProfileVersion);
        Assert.NotNull(idea.ScoredAt);
        // RenormalizedSubScore = (0.6×0.8 + 0.4×0.2) / 1.0 = 0.56 -> round(5.6) = 6
        Assert.Equal(6, idea.Score);
    }

    [Fact]
    public async Task ScoreSweepAsync_BelowThreshold_NoLlmCall_StampsVersion_EmbeddingDerivedScore()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile(version: 2));
        db.Ideas.Add(NewIdea(new float[] { 0, 1 })); // fit = 0.4 < 0.5
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        VerifyAnalyzed(analyzer, Times.Never());
        var idea = db.Ideas.Single();
        Assert.Empty(idea.PillarSubScores);
        Assert.Equal(2, idea.ScoredProfileVersion);
        Assert.Equal(4, idea.Score); // round(0.4 * 10)
    }

    [Fact]
    public async Task ScoreSweepAsync_ScoreAttemptsAtCap_IsSkipped()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile());
        db.Ideas.Add(NewIdea(new float[] { 1, 0 }, attempts: 3));
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        VerifyAnalyzed(analyzer, Times.Never());
    }

    [Fact]
    public async Task ScoreSweepAsync_AnalyzerReturnsNull_IncrementsAttempts_NoVersionStamp()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        analyzer.Setup(a => a.AnalyzeAsync(
                It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdeaAnalysis?)null);
        db.BrandRankingProfiles.Add(Profile());
        db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        var idea = db.Ideas.Single();
        Assert.Equal(1, idea.ScoreAttempts);
        Assert.Null(idea.ScoredProfileVersion);
    }

    [Fact]
    public async Task ScoreSweepAsync_BatchSize_BoundsLlmScoredItems()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile(version: 2));
        for (var i = 0; i < 5; i++) db.Ideas.Add(NewIdea(new float[] { 1, 0 })); // all above threshold
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        VerifyAnalyzed(analyzer, Times.Exactly(2));
        Assert.Equal(2, db.Ideas.Count(i => i.ScoredProfileVersion == 2));
    }

    [Fact]
    public async Task ScoreSweepAsync_NoCandidates_NoLlmCall_NoThrow()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile());
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        VerifyAnalyzed(analyzer, Times.Never());
    }

    [Fact]
    public async Task ScoreSweepAsync_ThrottleConfigured_StillScores()
    {
        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 1, ScoringEnabled = true }, Ranking());
        db.BrandRankingProfiles.Add(Profile(version: 2));
        db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
        await db.SaveChangesAsync();

        await svc.ScoreSweepAsync(CancellationToken.None);

        Assert.Equal(2, db.Ideas.Single().ScoredProfileVersion);
    }
}
