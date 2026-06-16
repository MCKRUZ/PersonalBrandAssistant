using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaDedupServiceTests
{
    private static (IdeaDedupService svc, ApplicationDbContext db) Build(
        ClusteringOptions options, RankingOptions ranking)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var monitor = Mock.Of<IOptionsMonitor<RankingOptions>>(m => m.CurrentValue == ranking);
        var svc = new IdeaDedupService(scopeFactory.Object, Options.Create(options), monitor,
            NullLogger<IdeaDedupService>.Instance);
        return (svc, db);
    }

    private static Idea Embedded(float[] embedding, int score = 5) => new()
    {
        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Embedding = embedding, Score = score,
        DuplicateOfId = null, ClusteredAt = null
    };

    private static ClusteringOptions Opts() => new() { LookbackHours = 48, MaxItemsPerSweep = 40 };
    private static RankingOptions Ranking(double threshold = 0.85) => new() { DedupThreshold = threshold };

    [Fact]
    public async Task DedupBatchAsync_SimilarPair_IsGrouped_DissimilarIsNot()
    {
        var (svc, db) = Build(Opts(), Ranking());
        var a = Embedded(new float[] { 1, 0 }, score: 8);
        var b = Embedded(new float[] { 1, 0 }, score: 5);    // cosine 1 with a -> duplicate
        var c = Embedded(new float[] { 0, 1 }, score: 7);    // cosine 0 with both -> standalone
        db.Ideas.AddRange(a, b, c);
        await db.SaveChangesAsync();

        await svc.DedupBatchAsync(CancellationToken.None);

        Assert.Equal(a.Id, db.Ideas.Single(i => i.Id == b.Id).DuplicateOfId);
        Assert.Null(db.Ideas.Single(i => i.Id == a.Id).DuplicateOfId);
        Assert.Null(db.Ideas.Single(i => i.Id == c.Id).DuplicateOfId);
        Assert.All(db.Ideas, i => Assert.NotNull(i.ClusteredAt));
    }

    [Fact]
    public async Task DedupBatchAsync_HighestBrandFit_IsPrimary()
    {
        var (svc, db) = Build(Opts(), Ranking());
        var low = Embedded(new float[] { 1, 0 }, score: 4);
        var high = Embedded(new float[] { 1, 0 }, score: 9);
        db.Ideas.AddRange(low, high);
        await db.SaveChangesAsync();

        await svc.DedupBatchAsync(CancellationToken.None);

        Assert.Equal(high.Id, db.Ideas.Single(i => i.Id == low.Id).DuplicateOfId);
        Assert.Null(db.Ideas.Single(i => i.Id == high.Id).DuplicateOfId);
    }

    [Fact]
    public async Task DedupBatchAsync_AnyInWindowUnembedded_IsGatedAndSetsNoDuplicate()
    {
        var (svc, db) = Build(Opts(), Ranking());
        var a = Embedded(new float[] { 1, 0 });
        var b = Embedded(new float[] { 1, 0 });
        var unembedded = new Idea
        {
            Title = "U", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
            Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Embedding = null
        };
        db.Ideas.AddRange(a, b, unembedded);
        await db.SaveChangesAsync();

        await svc.DedupBatchAsync(CancellationToken.None);

        Assert.All(db.Ideas, i => Assert.Null(i.DuplicateOfId)); // R-H1: gated, no grouping
        Assert.All(db.Ideas, i => Assert.Null(i.ClusteredAt));
    }

    [Fact]
    public async Task DedupBatchAsync_AlreadyDedupedOrClustered_AreNotReGrouped()
    {
        var (svc, db) = Build(Opts(), Ranking());
        var primary = Embedded(new float[] { 1, 0 }, score: 9);
        var alreadyDup = Embedded(new float[] { 1, 0 });
        alreadyDup.DuplicateOfId = Guid.NewGuid();
        var alreadyClustered = Embedded(new float[] { 1, 0 });
        alreadyClustered.ClusteredAt = DateTimeOffset.UtcNow.AddHours(-1);
        db.Ideas.AddRange(primary, alreadyDup, alreadyClustered);
        await db.SaveChangesAsync();

        await svc.DedupBatchAsync(CancellationToken.None);

        // Only `primary` is a candidate; < 2 candidates -> nothing changes.
        Assert.NotEqual(primary.Id, db.Ideas.Single(i => i.Id == alreadyDup.Id).DuplicateOfId);
        Assert.Null(db.Ideas.Single(i => i.Id == primary.Id).ClusteredAt);
    }

    [Fact]
    public async Task DedupBatchAsync_RespectsLookbackWindow()
    {
        var (svc, db) = Build(new ClusteringOptions { LookbackHours = 24, MaxItemsPerSweep = 40 }, Ranking());
        var inWindow = Embedded(new float[] { 1, 0 }, score: 8);
        var outOfWindow = Embedded(new float[] { 1, 0 }, score: 5);
        outOfWindow.DetectedAt = DateTimeOffset.UtcNow.AddHours(-48); // older than lookback
        db.Ideas.AddRange(inWindow, outOfWindow);
        await db.SaveChangesAsync();

        await svc.DedupBatchAsync(CancellationToken.None);

        // out-of-window excluded -> only one candidate -> no grouping
        Assert.Null(db.Ideas.Single(i => i.Id == inWindow.Id).DuplicateOfId);
        Assert.Null(db.Ideas.Single(i => i.Id == outOfWindow.Id).DuplicateOfId);
    }
}
