diff --git a/src/PBA.Api/Endpoints/IdeaEndpoints.cs b/src/PBA.Api/Endpoints/IdeaEndpoints.cs
index 33c1a26..e3a716f 100644
--- a/src/PBA.Api/Endpoints/IdeaEndpoints.cs
+++ b/src/PBA.Api/Endpoints/IdeaEndpoints.cs
@@ -30,7 +30,7 @@ public static class IdeaEndpoints
                 DateFrom = p.DateFrom,
                 DateTo = p.DateTo,
                 SearchText = p.SearchText,
-                SortBy = p.SortBy ?? "detectedat",
+                SortBy = p.SortBy ?? "rank",
                 SortDirection = p.SortDirection ?? "desc",
                 MinScore = p.MinScore,
                 IncludeDuplicates = p.IncludeDuplicates ?? false
diff --git a/src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs b/src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs
index bea2ff6..f9c87e5 100644
--- a/src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs
+++ b/src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs
@@ -19,4 +19,14 @@ public record IdeaDto
     public int? Score { get; init; }
     public string? ScoreReason { get; init; }
     public bool IsDuplicate { get; init; }
+
+    // Brand-anchored composite ranking (section-08), computed at read time from stored sub-scores +
+    // the active profile. Default 0 when no active profile exists (graceful degrade).
+    public double Rank { get; init; }
+    public double BrandFit { get; init; }
+    public double RecencyFactor { get; init; }
+    public IReadOnlyList<PillarBreakdownDto> PillarBreakdown { get; init; } = [];
+    public bool? IsAntiTopic { get; init; }
+    public bool? IsAuthorityTopic { get; init; }
+    public bool Stale { get; init; }
 }
diff --git a/src/PBA.Application/Features/Ideas/Dtos/PillarBreakdownDto.cs b/src/PBA.Application/Features/Ideas/Dtos/PillarBreakdownDto.cs
new file mode 100644
index 0000000..bc7ba3e
--- /dev/null
+++ b/src/PBA.Application/Features/Ideas/Dtos/PillarBreakdownDto.cs
@@ -0,0 +1,14 @@
+namespace PBA.Application.Features.Ideas.Dtos;
+
+/// <summary>
+/// One pillar's contribution to an idea's brand-fit, for display in the Ranked view. <see cref="Name"/>
+/// is resolved from the ACTIVE pillar by <c>BrandPillarId</c> (R-C3), so renames reflect without
+/// re-scoring; <see cref="Score"/> is the stored sub-score (0..1); <see cref="Reason"/> is the short
+/// LLM rationale (display only).
+/// </summary>
+public record PillarBreakdownDto
+{
+    public string Name { get; init; } = string.Empty;
+    public double Score { get; init; }
+    public string? Reason { get; init; }
+}
diff --git a/src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs b/src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs
index 56c076d..6211679 100644
--- a/src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs
+++ b/src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs
@@ -1,6 +1,6 @@
-using System.Linq.Expressions;
 using MediatR;
 using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common;
 using PBA.Application.Common.Interfaces;
 using PBA.Application.Common.Models;
 using PBA.Application.Features.Ideas.Dtos;
@@ -23,7 +23,7 @@ public static class ListIdeas
         public DateTimeOffset? DateFrom { get; init; }
         public DateTimeOffset? DateTo { get; init; }
         public string? SearchText { get; init; }
-        public string SortBy { get; init; } = "DetectedAt";
+        public string SortBy { get; init; } = "rank"; // brand-anchored rank is the default (R-M2)
         public string SortDirection { get; init; } = "desc";
         public int? MinScore { get; init; }
         public bool IncludeDuplicates { get; init; } = false;
@@ -71,14 +71,23 @@ public static class ListIdeas
             if (request.MinScore.HasValue)
                 query = query.Where(i => i.Score >= request.MinScore.Value);
 
+            // Filters push to SQL (R-C1b). Count over the filtered set before materializing.
             var totalCount = await query.CountAsync(cancellationToken);
 
-            query = ApplySort(query, request.SortBy, request.SortDirection);
+            // Snapshot the active profile once per query; the seeder guarantees one in practice. If none is
+            // active, every idea ranks 0 (graceful degrade) and the rank sort falls back to its DetectedAt
+            // tie-break, preserving recency ordering.
+            var profile = await db.BrandRankingProfiles.AsNoTracking()
+                .Include(p => p.Pillars)
+                .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
+            var snapshot = profile is null ? null : BrandRankingProfileSnapshot.FromProfile(profile);
 
-            var items = await query
-                .Skip((request.Page - 1) * request.PageSize)
-                .Take(request.PageSize)
-                .Select(i => new IdeaDto
+            // Projection MUST NOT select Embedding (R-C1a): select only display fields + ranking inputs.
+            // Materializing the full filtered set (~3,800 rows) is justified at this scale (R-C1b).
+            // Revisit trigger: ~25k rows OR p95 > 200 ms -> precompute a stored brandFit-per-version column
+            // and apply only decay live.
+            var rows = await query
+                .Select(i => new RankRow
                 {
                     Id = i.Id,
                     Title = i.Title,
@@ -94,36 +103,166 @@ public static class ListIdeas
                     HasSavedDetails = i.SavedDetails != null,
                     Score = i.Score,
                     ScoreReason = i.ScoreReason,
-                    IsDuplicate = i.DuplicateOfId != null
+                    IsDuplicate = i.DuplicateOfId != null,
+                    PillarSubScores = i.PillarSubScores,
+                    IsAntiTopic = i.IsAntiTopic,
+                    IsAuthorityTopic = i.IsAuthorityTopic,
+                    ScoredProfileVersion = i.ScoredProfileVersion
                 })
                 .ToListAsync(cancellationToken);
 
+            var now = DateTimeOffset.UtcNow;
+            var dtos = rows.Select(r => ToDto(r, snapshot, now)).ToList();
+
+            // Rank, sort, and page in memory (R-C1b).
+            var page = ApplySort(dtos, request.SortBy, request.SortDirection)
+                .Skip((request.Page - 1) * request.PageSize)
+                .Take(request.PageSize)
+                .ToList();
+
             return new PagedResult<IdeaDto>
             {
-                Items = items,
+                Items = page,
                 TotalCount = totalCount,
                 Page = request.Page,
                 PageSize = request.PageSize
             };
         }
 
-        private static IQueryable<Idea> ApplySort(IQueryable<Idea> query, string sortBy, string direction)
+        private static IdeaDto ToDto(RankRow r, BrandRankingProfileSnapshot? profile, DateTimeOffset now)
         {
-            var isDescending = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
+            var dto = new IdeaDto
+            {
+                Id = r.Id,
+                Title = r.Title,
+                Description = r.Description,
+                Url = r.Url,
+                SourceName = r.SourceName,
+                Category = r.Category,
+                Summary = r.Summary,
+                ThumbnailUrl = r.ThumbnailUrl,
+                Status = r.Status,
+                Tags = r.Tags,
+                DetectedAt = r.DetectedAt,
+                HasSavedDetails = r.HasSavedDetails,
+                Score = r.Score,
+                ScoreReason = r.ScoreReason,
+                IsDuplicate = r.IsDuplicate,
+                IsAntiTopic = r.IsAntiTopic,
+                IsAuthorityTopic = r.IsAuthorityTopic
+            };
+
+            if (profile is null) return dto; // no active profile -> rank 0, empty breakdown, not stale
+
+            // Sub-scores are keyed by BrandPillarId (R-C3). Last-wins guards a malformed duplicate key.
+            var subScores = r.PillarSubScores
+                .GroupBy(s => s.PillarId)
+                .ToDictionary(g => g.Key, g => g.Last());
+
+            var rank = ComputeRank(subScores, r.IsAntiTopic, r.IsAuthorityTopic,
+                r.ScoredProfileVersion, r.DetectedAt, profile, now);
+
+            return dto with
+            {
+                Rank = rank.Rank,
+                BrandFit = rank.BrandFit,
+                RecencyFactor = rank.RecencyFactor,
+                Stale = rank.Stale,
+                PillarBreakdown = rank.Breakdown
+            };
+        }
 
-            Expression<Func<Idea, object>> keySelector = sortBy.ToLower() switch
+        private static IEnumerable<IdeaDto> ApplySort(IEnumerable<IdeaDto> items, string sortBy, string direction)
+        {
+            var desc = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
+            return sortBy.ToLowerInvariant() switch
             {
-                "title" => i => i.Title,
-                "sourcename" => i => i.SourceName,
-                "category" => i => i.Category!,
-                "status" => i => i.Status,
-                "score" => i => (object)(i.Score ?? -1),
-                _ => i => i.DetectedAt
+                "title" => desc ? items.OrderByDescending(i => i.Title) : items.OrderBy(i => i.Title),
+                "sourcename" => desc ? items.OrderByDescending(i => i.SourceName) : items.OrderBy(i => i.SourceName),
+                "category" => desc ? items.OrderByDescending(i => i.Category) : items.OrderBy(i => i.Category),
+                "status" => desc ? items.OrderByDescending(i => i.Status) : items.OrderBy(i => i.Status),
+                "score" => desc ? items.OrderByDescending(i => i.Score ?? -1) : items.OrderBy(i => i.Score ?? -1),
+                "detectedat" => desc ? items.OrderByDescending(i => i.DetectedAt) : items.OrderBy(i => i.DetectedAt),
+                // Default = rank. Tie-break on DetectedAt desc so equal/zero ranks fall back to recency
+                // (e.g. when no profile is active or ideas are unscored).
+                _ => desc
+                    ? items.OrderByDescending(i => i.Rank).ThenByDescending(i => i.DetectedAt)
+                    : items.OrderBy(i => i.Rank).ThenByDescending(i => i.DetectedAt)
             };
+        }
+    }
 
-            return isDescending
-                ? query.OrderByDescending(keySelector)
-                : query.OrderBy(keySelector);
+    /// <summary>
+    /// Pure brand-anchored composite rank: <c>brandFit × recencyDecay × antiTopicMultiplier ×
+    /// authorityBoost</c>. No DB, no LLM — deterministic and unit-tested in isolation. brandFit reuses the
+    /// shared <see cref="BrandFit.RenormalizedSubScore"/> over the ACTIVE pillars (R-C2a/b); read-time
+    /// renormalization means a weight edit re-ranks instantly with zero LLM calls.
+    /// </summary>
+    internal static RankResult ComputeRank(
+        IReadOnlyDictionary<Guid, PillarSubScore> subScores,
+        bool? isAntiTopic,
+        bool? isAuthorityTopic,
+        int? scoredProfileVersion,
+        DateTimeOffset detectedAt,
+        BrandRankingProfileSnapshot profile,
+        DateTimeOffset now)
+    {
+        // Breakdown + sub-score map cover only pillars present in the ACTIVE profile that the idea has a
+        // sub-score for (R-C2a); display name comes from the active pillar (R-C3).
+        var breakdown = new List<PillarBreakdownDto>();
+        var subById = new Dictionary<Guid, double>();
+        foreach (var pillar in profile.Pillars)
+        {
+            if (!subScores.TryGetValue(pillar.Id, out var sub)) continue;
+            subById[pillar.Id] = sub.Score;
+            breakdown.Add(new PillarBreakdownDto { Name = pillar.Name, Score = sub.Score, Reason = sub.Reason });
         }
+
+        var brandFit = Math.Clamp(
+            BrandFit.RenormalizedSubScore(subById, profile.Pillars.Select(p => (p.Id, p.Weight)).ToList()),
+            0, 1); // R-M6
+
+        var ageDays = Math.Max(0, (now - detectedAt).TotalDays);
+        var decay = profile.HalfLifeDays > 0
+            ? Math.Exp(-Math.Log(2) / profile.HalfLifeDays * ageDays)
+            : 0.0;
+        var recency = Math.Max(decay, profile.DecayFloor);
+
+        var antiMult = isAntiTopic == true ? profile.AntiTopicMultiplier : 1.0;   // R-L1
+        var authBoost = isAuthorityTopic == true ? profile.AuthorityBoost : 1.0;  // R-L1
+
+        var rank = brandFit * recency * antiMult * authBoost;
+        var stale = scoredProfileVersion is { } v && v < profile.Version; // R-C2c
+
+        return new RankResult(rank, brandFit, recency, stale, breakdown);
+    }
+
+    internal readonly record struct RankResult(
+        double Rank, double BrandFit, double RecencyFactor, bool Stale,
+        IReadOnlyList<PillarBreakdownDto> Breakdown);
+
+    // Embedding-free read-path projection: every column the rank + DTO need, and deliberately NOT
+    // Embedding (R-C1a).
+    private sealed class RankRow
+    {
+        public Guid Id { get; init; }
+        public string Title { get; init; } = string.Empty;
+        public string? Description { get; init; }
+        public string? Url { get; init; }
+        public string SourceName { get; init; } = string.Empty;
+        public string? Category { get; init; }
+        public string? Summary { get; init; }
+        public string? ThumbnailUrl { get; init; }
+        public IdeaStatus Status { get; init; }
+        public IReadOnlyList<string> Tags { get; init; } = [];
+        public DateTimeOffset DetectedAt { get; init; }
+        public bool HasSavedDetails { get; init; }
+        public int? Score { get; init; }
+        public string? ScoreReason { get; init; }
+        public bool IsDuplicate { get; init; }
+        public IList<PillarSubScore> PillarSubScores { get; init; } = [];
+        public bool? IsAntiTopic { get; init; }
+        public bool? IsAuthorityTopic { get; init; }
+        public int? ScoredProfileVersion { get; init; }
     }
 }
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Queries/ComputeRankTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Queries/ComputeRankTests.cs
new file mode 100644
index 0000000..1d66566
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/Ideas/Queries/ComputeRankTests.cs
@@ -0,0 +1,140 @@
+using PBA.Application.Common.Models;
+using PBA.Application.Features.Ideas.Queries;
+using PBA.Domain.Entities;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.Ideas.Queries;
+
+public class ComputeRankTests
+{
+    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
+    private static readonly Guid P2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
+
+    private static BrandRankingProfileSnapshot Profile(
+        double w1 = 0.6, double w2 = 0.4, int version = 2,
+        double halfLife = 7, double floor = 0.075, double antiMult = 0.1, double authBoost = 1.2) => new(
+        Version: version,
+        Positioning: "P", AudiencePrimary: "A", AudienceSecondary: null,
+        Pillars:
+        [
+            new BrandPillarSnapshot(P1, "Pillar One", "d1", w1, 0, null),
+            new BrandPillarSnapshot(P2, "Pillar Two", "d2", w2, 1, null),
+        ],
+        AuthorityTopics: [], AntiTopics: [], VoiceMarkers: [],
+        HalfLifeDays: halfLife, DecayFloor: floor, AntiTopicMultiplier: antiMult, AuthorityBoost: authBoost);
+
+    private static Dictionary<Guid, PillarSubScore> Subs(params (Guid id, double score)[] subs) =>
+        subs.ToDictionary(s => s.id, s => new PillarSubScore
+        {
+            PillarId = s.id, PillarName = "n", Score = s.score, Reason = "r"
+        });
+
+    [Fact]
+    public void ComputeRank_WeightedSumOverActivePillars_GivesRenormalizedBrandFit()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(
+            Subs((P1, 0.8), (P2, 0.2)), null, null, 2, now, Profile(), now);
+
+        // (0.6*0.8 + 0.4*0.2) / (0.6+0.4) = 0.56; age 0 -> recency 1; no flags -> rank == brandFit
+        Assert.Equal(0.56, r.BrandFit, 6);
+        Assert.Equal(0.56, r.Rank, 6);
+    }
+
+    [Fact]
+    public void ComputeRank_OnlySomePillarsScored_RenormalizesOverPresentPillars()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(Subs((P1, 0.8)), null, null, 2, now, Profile(), now);
+
+        // only P1 present: (0.6*0.8)/0.6 = 0.8 — scale stays comparable (R-C2b/c)
+        Assert.Equal(0.8, r.BrandFit, 6);
+        Assert.Single(r.Breakdown);
+        Assert.Equal("Pillar One", r.Breakdown[0].Name);
+    }
+
+    [Theory]
+    [InlineData(0, 1.0)]      // age 0 -> 1.0
+    [InlineData(7, 0.5)]      // age == half-life -> 0.5
+    [InlineData(14, 0.25)]    // two half-lives -> 0.25
+    [InlineData(1000, 0.075)] // very old -> floor
+    public void ComputeRank_Recency_FollowsHalfLifeWithFloor(double ageDays, double expected)
+    {
+        var now = DateTimeOffset.UtcNow;
+        var detectedAt = now.AddDays(-ageDays);
+        var r = ListIdeas.ComputeRank(
+            Subs((P1, 1.0), (P2, 1.0)), null, null, 2, detectedAt, Profile(), now);
+
+        Assert.Equal(expected, r.RecencyFactor, 3);
+    }
+
+    [Fact]
+    public void ComputeRank_AntiTopicFlag_AppliesMultiplier()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(Subs((P1, 1.0), (P2, 1.0)), isAntiTopic: true, null, 2, now, Profile(), now);
+
+        Assert.Equal(0.1, r.Rank, 6); // brandFit 1 × recency 1 × antiMult 0.1
+    }
+
+    [Fact]
+    public void ComputeRank_NullAntiTopicFlag_NoMultiplier()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(Subs((P1, 1.0), (P2, 1.0)), isAntiTopic: null, null, 2, now, Profile(), now);
+
+        Assert.Equal(1.0, r.Rank, 6);
+    }
+
+    [Fact]
+    public void ComputeRank_AuthorityFlag_AppliesBoost()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(Subs((P1, 1.0), (P2, 1.0)), null, isAuthorityTopic: true, 2, now, Profile(), now);
+
+        Assert.Equal(1.2, r.Rank, 6); // brandFit 1 × recency 1 × authBoost 1.2
+    }
+
+    [Fact]
+    public void ComputeRank_BrandFitClampedToOne()
+    {
+        var now = DateTimeOffset.UtcNow;
+        // A malformed sub-score above 1 renormalizes above 1; must clamp (R-M6).
+        var r = ListIdeas.ComputeRank(Subs((P1, 2.0)), null, null, 2, now, Profile(), now);
+
+        Assert.Equal(1.0, r.BrandFit, 6);
+    }
+
+    [Fact]
+    public void ComputeRank_Unscored_RanksZero()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(Subs(), null, null, 2, now, Profile(), now);
+
+        Assert.Equal(0, r.BrandFit, 6);
+        Assert.Equal(0, r.Rank, 6);
+        Assert.Empty(r.Breakdown);
+    }
+
+    [Fact]
+    public void ComputeRank_OlderProfileVersion_IsStale()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var profile = Profile(version: 3);
+
+        Assert.True(ListIdeas.ComputeRank(Subs((P1, 0.5)), null, null, 2, now, profile, now).Stale);
+        Assert.False(ListIdeas.ComputeRank(Subs((P1, 0.5)), null, null, 3, now, profile, now).Stale);
+        Assert.False(ListIdeas.ComputeRank(Subs((P1, 0.5)), null, null, null, now, profile, now).Stale);
+    }
+
+    [Fact]
+    public void ComputeRank_BreakdownUsesActivePillarNameAndStoredScore()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var r = ListIdeas.ComputeRank(Subs((P1, 0.75), (P2, 0.25)), null, null, 2, now, Profile(), now);
+
+        var p1 = r.Breakdown.Single(b => b.Name == "Pillar One");
+        Assert.Equal(0.75, p1.Score, 6);
+        Assert.Equal("r", p1.Reason);
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
index 1a91876..1434bc1 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
@@ -338,4 +338,117 @@ public class ListIdeasHandlerTests
         Assert.Contains(result.Value.Items, i => i.IsDuplicate);
         Assert.Contains(result.Value.Items, i => !i.IsDuplicate);
     }
+
+    // --- Section-08: brand-anchored rank ----------------------------------------------------------
+
+    private static readonly Guid Pillar1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
+
+    private static void AddActiveProfile(ApplicationDbContext context, int version = 1, double weight = 1.0)
+    {
+        context.BrandRankingProfiles.Add(new BrandRankingProfile
+        {
+            Version = version, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+            Positioning = "P", AudiencePrimary = "A",
+            HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+            Pillars = [new BrandPillar { Id = Pillar1, Name = "Pillar One", Description = "d", Weight = weight, Order = 0 }]
+        });
+    }
+
+    private static Idea Scored(string title, double subScore, DateTimeOffset? detectedAt = null,
+        int scoredVersion = 1, bool? anti = null, bool? authority = null) => new()
+    {
+        Title = title, SourceName = "test-source", DeduplicationKey = Guid.NewGuid().ToString(),
+        Status = IdeaStatus.New, DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
+        ScoredProfileVersion = scoredVersion, IsAntiTopic = anti, IsAuthorityTopic = authority,
+        PillarSubScores = [new PillarSubScore { PillarId = Pillar1, PillarName = "Pillar One", Score = subScore, Reason = "fits" }]
+    };
+
+    [Fact]
+    public async Task Handle_DefaultSort_IsRank_OrdersByCompositeRankDescending()
+    {
+        await using var context = CreateContext();
+        AddActiveProfile(context);
+        context.Ideas.Add(Scored("Low", 0.2));
+        context.Ideas.Add(Scored("High", 0.9));
+        context.Ideas.Add(Scored("Mid", 0.5));
+        await context.SaveChangesAsync();
+
+        var handler = new ListIdeas.Handler(context);
+        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None); // no SortBy
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("High", result.Value!.Items[0].Title);
+        Assert.Equal("Mid", result.Value.Items[1].Title);
+        Assert.Equal("Low", result.Value.Items[2].Title);
+    }
+
+    [Fact]
+    public async Task Handle_ActiveProfile_PopulatesRankFieldsAndBreakdown()
+    {
+        await using var context = CreateContext();
+        AddActiveProfile(context);
+        context.Ideas.Add(Scored("Item", 0.8, authority: true));
+        await context.SaveChangesAsync();
+
+        var handler = new ListIdeas.Handler(context);
+        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);
+
+        var dto = Assert.Single(result.Value!.Items);
+        Assert.Equal(0.8, dto.BrandFit, 6);
+        Assert.True(dto.Rank > 0);
+        Assert.True(dto.RecencyFactor > 0);
+        Assert.True(dto.IsAuthorityTopic);
+        var breakdown = Assert.Single(dto.PillarBreakdown);
+        Assert.Equal("Pillar One", breakdown.Name);
+        Assert.Equal(0.8, breakdown.Score, 6);
+    }
+
+    [Fact]
+    public async Task Handle_IdeaScoredAtOlderVersion_SurfacesStale()
+    {
+        await using var context = CreateContext();
+        AddActiveProfile(context, version: 2);
+        context.Ideas.Add(Scored("Stale", 0.5, scoredVersion: 1));
+        context.Ideas.Add(Scored("Fresh", 0.5, scoredVersion: 2));
+        await context.SaveChangesAsync();
+
+        var handler = new ListIdeas.Handler(context);
+        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);
+
+        Assert.True(result.Value!.Items.Single(i => i.Title == "Stale").Stale);
+        Assert.False(result.Value.Items.Single(i => i.Title == "Fresh").Stale);
+    }
+
+    [Fact]
+    public async Task Handle_IdeaWithEmbedding_RanksWithoutSelectingTheVector()
+    {
+        await using var context = CreateContext();
+        AddActiveProfile(context);
+        var idea = Scored("Embedded", 0.7);
+        idea.Embedding = new float[] { 0.1f, 0.2f }; // present in the row, must NOT break the projection (R-C1a)
+        context.Ideas.Add(idea);
+        await context.SaveChangesAsync();
+
+        var handler = new ListIdeas.Handler(context);
+        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);
+
+        var dto = Assert.Single(result.Value!.Items);
+        Assert.Equal(0.7, dto.BrandFit, 6); // ranked fine; IdeaDto has no embedding field by construction
+    }
+
+    [Fact]
+    public async Task Handle_NoActiveProfile_RanksZero_AndFallsBackToRecencyOrder()
+    {
+        await using var context = CreateContext();
+        var now = DateTimeOffset.UtcNow;
+        context.Ideas.Add(Scored("Older", 0.9, detectedAt: now.AddDays(-2)));
+        context.Ideas.Add(Scored("Newer", 0.1, detectedAt: now));
+        await context.SaveChangesAsync();
+
+        var handler = new ListIdeas.Handler(context);
+        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);
+
+        Assert.All(result.Value!.Items, i => Assert.Equal(0, i.Rank));
+        Assert.Equal("Newer", result.Value.Items[0].Title); // rank-tie -> DetectedAt desc
+    }
 }
