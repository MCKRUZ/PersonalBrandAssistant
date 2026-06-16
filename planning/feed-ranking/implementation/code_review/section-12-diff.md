diff --git a/src/PBA.Api/appsettings.json b/src/PBA.Api/appsettings.json
index 83e48b4..58b114f 100644
--- a/src/PBA.Api/appsettings.json
+++ b/src/PBA.Api/appsettings.json
@@ -58,7 +58,8 @@
     "IntervalMinutes": 10,
     "BatchSize": 20,
     "ThrottleMs": 1000,
-    "Model": "google/gemini-2.5-flash"
+    "Model": "google/gemini-2.5-flash",
+    "ScoringEnabled": false
   },
   "Clustering": {
     "IntervalMinutes": 30,
diff --git a/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs b/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
index f23b735..de7c102 100644
--- a/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
+++ b/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
@@ -15,4 +15,12 @@ public sealed class IdeaScoringOptions
 
     /// <summary>Cheap, fast model for per-idea scoring. Defaults independent of the drafting model.</summary>
     public string Model { get; init; } = "google/gemini-2.5-flash";
+
+    /// <summary>
+    /// Gates the irreversible, token-spending LLM scoring step (replaces the old BackfillEnabled). Default
+    /// FALSE so the first production LLM-scoring run never fires automatically on deploy — embedding and the
+    /// below-threshold embedding-only brandFit still run (cheap, reversible). A human flips this to true on
+    /// the target host after confirming embedding backfill completed and the cost is understood (R: section-12 gate).
+    /// </summary>
+    public bool ScoringEnabled { get; init; } = false;
 }
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs b/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs
index f393352..438ff2a 100644
--- a/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs
+++ b/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs
@@ -99,6 +99,12 @@ public sealed class IdeaScoringService(
             .ToList();
         var below = prefiltered.Where(x => x.fit < ranking.PreFilterThreshold).ToList();
 
+        // The LLM scoring step is GATED (section-12): the first production run spends real tokens and is
+        // irreversible. When ScoringEnabled is false the above-threshold items are left unscored (no attempt,
+        // no version stamp) so they are reconsidered once a human enables scoring; embedding + the
+        // below-threshold embedding-only brandFit below still run (cheap, reversible).
+        if (!_options.ScoringEnabled) above = [];
+
         var changed = false;
         var llmScored = 0;
 
diff --git a/tests/PBA.Infrastructure.Tests/Cutover/CutoverTests.cs b/tests/PBA.Infrastructure.Tests/Cutover/CutoverTests.cs
new file mode 100644
index 0000000..23176e8
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Cutover/CutoverTests.cs
@@ -0,0 +1,163 @@
+using System.Text.Json;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using Npgsql;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Services.Radar;
+using Testcontainers.PostgreSql;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Cutover;
+
+/// <summary>
+/// Section-12 cutover guards: migrations apply in order on real Postgres, the dead V1 config is gone
+/// (R-L6), and the irreversible LLM-scoring step stays gated until a human enables it.
+/// </summary>
+public class CutoverTests
+{
+    // Test 1 — migrations apply cleanly + in order on a real (pgvector-capable) Postgres. EF InMemory can't
+    // run pgvector DDL, so this is the only place the extension-before-vector-columns order is proven.
+    [Fact]
+    public async Task Migrations_ApplyCleanly_OnRealPostgres()
+    {
+        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
+        await container.StartAsync();
+
+        var dataSourceBuilder = new NpgsqlDataSourceBuilder(container.GetConnectionString());
+        dataSourceBuilder.EnableDynamicJson();
+        dataSourceBuilder.UseVector();
+        await using var dataSource = dataSourceBuilder.Build();
+
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseNpgsql(dataSource, o => o.UseVector())
+            .Options;
+
+        await using (var db = new ApplicationDbContext(options))
+        {
+            await db.Database.MigrateAsync(); // must not throw — implicitly proves apply order
+        }
+
+        await using var conn = dataSource.CreateConnection();
+        await conn.OpenAsync();
+
+        Assert.True(await ScalarBoolAsync(conn,
+            "SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector')"), "vector extension");
+        Assert.True(await ScalarBoolAsync(conn,
+            "SELECT to_regclass('public.\"BrandRankingProfiles\"') IS NOT NULL"), "BrandRankingProfiles table");
+        Assert.True(await ScalarBoolAsync(conn,
+            "SELECT to_regclass('public.\"BrandPillars\"') IS NOT NULL"), "BrandPillars table");
+
+        foreach (var column in new[] { "Embedding", "EmbeddedAt", "PillarSubScores", "ScoredProfileVersion", "ScoreAttempts" })
+        {
+            Assert.True(await ScalarBoolAsync(conn,
+                    $"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name = 'Ideas' AND column_name = '{column}')"),
+                $"Ideas.{column} column");
+        }
+
+        // Partial unique index enforcing a single active profile (R-L4).
+        Assert.True(await ScalarBoolAsync(conn,
+            "SELECT EXISTS(SELECT 1 FROM pg_indexes WHERE tablename = 'BrandRankingProfiles' " +
+            "AND indexdef ILIKE '%unique%' AND indexdef ILIKE '%IsActive%')"), "partial unique index on IsActive");
+    }
+
+    // Test 2 — dead V1 scoring/clustering config removed (R-L6); the deliberate survivors stay.
+    [Fact]
+    public void Appsettings_DeadScoringAndClusteringKeys_AreRemoved()
+    {
+        using var doc = JsonDocument.Parse(File.ReadAllText(FindAppsettingsPath()));
+        var root = doc.RootElement;
+
+        var ideaScoring = root.GetProperty("IdeaScoring");
+        Assert.False(ideaScoring.TryGetProperty("BackfillEnabled", out _), "BackfillEnabled must be removed");
+
+        Assert.True(root.TryGetProperty("Clustering", out var clustering), "Clustering section stays (decision #3)");
+        Assert.False(clustering.TryGetProperty("MinScore", out _), "Clustering:MinScore must be removed");
+
+        // Unrelated key that must NOT be touched.
+        Assert.True(root.GetProperty("HackerNews").TryGetProperty("MinScore", out _), "HackerNews:MinScore stays");
+    }
+
+    // Test 3 — the first prod LLM-scoring run is gated: with ScoringEnabled false the sweep skips AnalyzeAsync
+    // entirely; flipping it true invokes the analyzer.
+    [Theory]
+    [InlineData(false, 0)]
+    [InlineData(true, 1)]
+    public async Task ScoringSweep_InvokesLlm_OnlyWhenScoringGateEnabled(bool scoringEnabled, int expectedCalls)
+    {
+        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+        var pillarId = Guid.NewGuid();
+        db.BrandRankingProfiles.Add(new BrandRankingProfile
+        {
+            Version = 1, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+            Positioning = "P", AudiencePrimary = "A",
+            HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+            Pillars = [new BrandPillar { Id = pillarId, Name = "P", Description = "d", Weight = 1, Order = 0,
+                DescriptionEmbedding = new float[] { 1, 0 } }]
+        });
+        db.Ideas.Add(new Idea
+        {
+            Title = "T", Description = "D", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
+            Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Embedding = new float[] { 1, 0 }
+        });
+        await db.SaveChangesAsync();
+
+        var analyzer = new Mock<IIdeaAnalyzer>();
+        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new IdeaAnalysis([new PillarScore(pillarId, "P", 0.8, "r")], false, false, "r"));
+
+        var embedSidecar = new Mock<ISidecarClient>();
+        embedSidecar.Setup(s => s.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((IReadOnlyList<float[]>)new List<float[]>());
+        var embedder = new IdeaEmbeddingService(db, embedSidecar.Object,
+            Mock.Of<IOptionsMonitor<EmbeddingOptions>>(m => m.CurrentValue == new EmbeddingOptions()),
+            NullLogger<IdeaEmbeddingService>.Instance);
+
+        var sp = new Mock<IServiceProvider>();
+        sp.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
+        sp.Setup(p => p.GetService(typeof(IIdeaAnalyzer))).Returns(analyzer.Object);
+        sp.Setup(p => p.GetService(typeof(IdeaEmbeddingService))).Returns(embedder);
+        var scope = new Mock<IServiceScope>();
+        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
+        var scopeFactory = new Mock<IServiceScopeFactory>();
+        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
+
+        var svc = new IdeaScoringService(scopeFactory.Object,
+            Options.Create(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = scoringEnabled }),
+            Mock.Of<IOptionsMonitor<RankingOptions>>(m => m.CurrentValue == new RankingOptions { PreFilterThreshold = 0.5, ScoringWindowDays = 30 }),
+            NullLogger<IdeaScoringService>.Instance);
+
+        await svc.ScoreSweepAsync(CancellationToken.None);
+
+        analyzer.Verify(a => a.AnalyzeAsync(
+            It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()),
+            Times.Exactly(expectedCalls));
+    }
+
+    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection conn, string sql)
+    {
+        await using var cmd = new NpgsqlCommand(sql, conn);
+        return (bool)(await cmd.ExecuteScalarAsync())!;
+    }
+
+    private static string FindAppsettingsPath()
+    {
+        var dir = new DirectoryInfo(AppContext.BaseDirectory);
+        while (dir is not null)
+        {
+            var candidate = Path.Combine(dir.FullName, "src", "PBA.Api", "appsettings.json");
+            if (File.Exists(candidate)) return candidate;
+            dir = dir.Parent;
+        }
+        throw new FileNotFoundException("Could not locate src/PBA.Api/appsettings.json from the test directory.");
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj b/tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj
index c87291f..4d0d5df 100644
--- a/tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj
+++ b/tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj
@@ -13,6 +13,7 @@
     <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.7" />
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
     <PackageReference Include="Moq" Version="4.20.72" />
+    <PackageReference Include="Testcontainers.PostgreSql" Version="4.12.0" />
     <PackageReference Include="xunit" Version="2.9.3" />
     <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
   </ItemGroup>
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
index 8472140..cc6819b 100644
--- a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
@@ -98,7 +98,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_CandidateSet_ExcludesOutOfWindowUnembeddedAndCurrentVersion()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile(version: 2));
         var valid = NewIdea(new float[] { 1, 0 });
         var old = NewIdea(new float[] { 1, 0 }, detectedAt: DateTimeOffset.UtcNow.AddDays(-40));
@@ -119,7 +119,7 @@ public class IdeaScoringServiceTests
     public async Task ScoreSweepAsync_SnapshotsRankingOptionsOnce()
     {
         var counting = new CountingMonitor<RankingOptions>(Ranking());
-        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 },
+        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true },
             new RankingOptions(), rankingMonitor: counting);
         db.BrandRankingProfiles.Add(Profile());
         db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
@@ -133,7 +133,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_AboveThreshold_StoresSubScoresFlagsVersionAndDerivedScore()
     {
-        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile(version: 2));
         db.Ideas.Add(NewIdea(new float[] { 1, 0 })); // fit = 0.6 >= 0.5
         await db.SaveChangesAsync();
@@ -155,7 +155,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_BelowThreshold_NoLlmCall_StampsVersion_EmbeddingDerivedScore()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile(version: 2));
         db.Ideas.Add(NewIdea(new float[] { 0, 1 })); // fit = 0.4 < 0.5
         await db.SaveChangesAsync();
@@ -172,7 +172,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_ScoreAttemptsAtCap_IsSkipped()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile());
         db.Ideas.Add(NewIdea(new float[] { 1, 0 }, attempts: 3));
         await db.SaveChangesAsync();
@@ -185,7 +185,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_AnalyzerReturnsNull_IncrementsAttempts_NoVersionStamp()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         analyzer.Setup(a => a.AnalyzeAsync(
                 It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((IdeaAnalysis?)null);
@@ -203,7 +203,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_BatchSize_BoundsLlmScoredItems()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0 }, Ranking());
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile(version: 2));
         for (var i = 0; i < 5; i++) db.Ideas.Add(NewIdea(new float[] { 1, 0 })); // all above threshold
         await db.SaveChangesAsync();
@@ -217,7 +217,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_NoCandidates_NoLlmCall_NoThrow()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile());
         await db.SaveChangesAsync();
 
@@ -229,7 +229,7 @@ public class IdeaScoringServiceTests
     [Fact]
     public async Task ScoreSweepAsync_ThrottleConfigured_StillScores()
     {
-        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 1 }, Ranking());
+        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 1, ScoringEnabled = true }, Ranking());
         db.BrandRankingProfiles.Add(Profile(version: 2));
         db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
         await db.SaveChangesAsync();
