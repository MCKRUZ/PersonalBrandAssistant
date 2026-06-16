using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;
using Testcontainers.PostgreSql;
using Xunit;

namespace PBA.Infrastructure.Tests.Cutover;

/// <summary>
/// Section-12 cutover guards: migrations apply in order on real Postgres, the dead V1 config is gone
/// (R-L6), and the irreversible LLM-scoring step stays gated until a human enables it.
/// </summary>
public class CutoverTests
{
    // Test 1 — migrations apply cleanly + in order on a real (pgvector-capable) Postgres. EF InMemory can't
    // run pgvector DDL, so this is the only place the extension-before-vector-columns order is proven.
    [Fact]
    public async Task Migrations_ApplyCleanly_OnRealPostgres()
    {
        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await container.StartAsync();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(container.GetConnectionString());
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.MigrateAsync(); // must not throw — implicitly proves apply order
        }

        await using var conn = dataSource.CreateConnection();
        await conn.OpenAsync();

        Assert.True(await ScalarBoolAsync(conn,
            "SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector')"), "vector extension");
        Assert.True(await ScalarBoolAsync(conn,
            "SELECT to_regclass('public.\"BrandRankingProfiles\"') IS NOT NULL"), "BrandRankingProfiles table");
        Assert.True(await ScalarBoolAsync(conn,
            "SELECT to_regclass('public.\"BrandPillars\"') IS NOT NULL"), "BrandPillars table");

        foreach (var column in new[] { "Embedding", "EmbeddedAt", "PillarSubScores", "ScoredProfileVersion", "ScoreAttempts" })
        {
            Assert.True(await ScalarBoolAsync(conn,
                    $"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name = 'Ideas' AND column_name = '{column}')"),
                $"Ideas.{column} column");
        }

        // Partial UNIQUE index enforcing a single active profile (R-L4): must be unique, on IsActive, AND
        // partial (a WHERE predicate) — a plain unique index on IsActive would wrongly forbid >1 inactive row.
        Assert.True(await ScalarBoolAsync(conn,
            "SELECT EXISTS(SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid " +
            "WHERE c.relname = 'BrandRankingProfiles' AND i.indisunique AND i.indpred IS NOT NULL)"),
            "partial unique index on BrandRankingProfiles");
    }

    // Test 2 — dead V1 scoring/clustering config removed (R-L6); the deliberate survivors stay.
    [Fact]
    public void Appsettings_DeadScoringAndClusteringKeys_AreRemoved()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindAppsettingsPath()));
        var root = doc.RootElement;

        var ideaScoring = root.GetProperty("IdeaScoring");
        Assert.False(ideaScoring.TryGetProperty("BackfillEnabled", out _), "BackfillEnabled must be removed");

        Assert.True(root.TryGetProperty("Clustering", out var clustering), "Clustering section stays (decision #3)");
        Assert.False(clustering.TryGetProperty("MinScore", out _), "Clustering:MinScore must be removed");

        // Unrelated key that must NOT be touched.
        Assert.True(root.GetProperty("HackerNews").TryGetProperty("MinScore", out _), "HackerNews:MinScore stays");
    }

    // Test 3 — the first prod LLM-scoring run is gated: with ScoringEnabled false the sweep skips AnalyzeAsync
    // entirely; flipping it true invokes the analyzer.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task ScoringSweep_InvokesLlm_OnlyWhenScoringGateEnabled(bool scoringEnabled, int expectedCalls)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var pillarId = Guid.NewGuid();
        db.BrandRankingProfiles.Add(new BrandRankingProfile
        {
            Version = 1, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
            Positioning = "P", AudiencePrimary = "A",
            HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
            Pillars = [new BrandPillar { Id = pillarId, Name = "P", Description = "d", Weight = 1, Order = 0,
                DescriptionEmbedding = new float[] { 1, 0 } }]
        });
        db.Ideas.Add(new Idea
        {
            Title = "T", Description = "D", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
            Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Embedding = new float[] { 1, 0 }
        });
        await db.SaveChangesAsync();

        var analyzer = new Mock<IIdeaAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdeaAnalysis([new PillarScore(pillarId, "P", 0.8, "r")], false, false, "r"));

        var embedSidecar = new Mock<ISidecarClient>();
        embedSidecar.Setup(s => s.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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

        var svc = new IdeaScoringService(scopeFactory.Object,
            Options.Create(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = scoringEnabled }),
            Mock.Of<IOptionsMonitor<RankingOptions>>(m => m.CurrentValue == new RankingOptions { PreFilterThreshold = 0.5, ScoringWindowDays = 30 }),
            NullLogger<IdeaScoringService>.Instance);

        await svc.ScoreSweepAsync(CancellationToken.None);

        analyzer.Verify(a => a.AnalyzeAsync(
            It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCalls));

        // The load-bearing invariant: a gated-off sweep must leave the above-threshold idea fully
        // reconsiderable — no version stamp, no burned attempt — so it scores the moment the gate flips.
        var idea = db.Ideas.Single();
        if (!scoringEnabled)
        {
            Assert.Null(idea.ScoredProfileVersion);
            Assert.Equal(0, idea.ScoreAttempts);
        }
        else
        {
            Assert.Equal(1, idea.ScoredProfileVersion);
        }
    }

    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static string FindAppsettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PBA.Api", "appsettings.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate src/PBA.Api/appsettings.json from the test directory.");
    }
}
