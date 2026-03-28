diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IEngagementAggregator.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IEngagementAggregator.cs
new file mode 100644
index 0000000..9ef74f4
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IEngagementAggregator.cs
@@ -0,0 +1,16 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IEngagementAggregator
+{
+    Task<Result<EngagementSnapshot>> FetchLatestAsync(Guid contentPlatformStatusId, CancellationToken ct);
+
+    Task<Result<ContentPerformanceReport>> GetPerformanceAsync(Guid contentId, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<TopPerformingContent>>> GetTopContentAsync(
+        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
+
+    Task<Result<int>> CleanupSnapshotsAsync(CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
index 52e43f8..135999d 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
@@ -9,4 +9,8 @@ public class ContentEngineOptions
     public int BrandVoiceScoreThreshold { get; set; } = 70;
 
     public int MaxAutoRegenerateAttempts { get; set; } = 3;
+
+    public int EngagementRetentionDays { get; set; } = 30;
+
+    public int EngagementAggregationIntervalHours { get; set; } = 4;
 }
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentPerformanceReport.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentPerformanceReport.cs
new file mode 100644
index 0000000..d620ad1
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentPerformanceReport.cs
@@ -0,0 +1,11 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record ContentPerformanceReport(
+    Guid ContentId,
+    IReadOnlyDictionary<PlatformType, EngagementSnapshot> LatestByPlatform,
+    int TotalEngagement,
+    decimal? LlmCost,
+    decimal? CostPerEngagement);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs b/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs
new file mode 100644
index 0000000..60133f0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs
@@ -0,0 +1,9 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record TopPerformingContent(
+    Guid ContentId,
+    string Title,
+    int TotalEngagement,
+    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform);
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/EngagementAggregator.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/EngagementAggregator.cs
new file mode 100644
index 0000000..8b1c429
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/EngagementAggregator.cs
@@ -0,0 +1,236 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+public class EngagementAggregator : IEngagementAggregator
+{
+    private readonly IApplicationDbContext _db;
+    private readonly IEnumerable<ISocialPlatform> _platforms;
+    private readonly IRateLimiter _rateLimiter;
+    private readonly ILogger<EngagementAggregator> _logger;
+    private readonly ContentEngineOptions _options;
+
+    public EngagementAggregator(
+        IApplicationDbContext db,
+        IEnumerable<ISocialPlatform> platforms,
+        IRateLimiter rateLimiter,
+        IOptions<ContentEngineOptions> options,
+        ILogger<EngagementAggregator> logger)
+    {
+        _db = db;
+        _platforms = platforms;
+        _rateLimiter = rateLimiter;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task<Result<EngagementSnapshot>> FetchLatestAsync(
+        Guid contentPlatformStatusId, CancellationToken ct)
+    {
+        var cps = await _db.ContentPlatformStatuses
+            .FirstOrDefaultAsync(s => s.Id == contentPlatformStatusId, ct);
+
+        if (cps is null)
+            return Result<EngagementSnapshot>.NotFound(
+                $"ContentPlatformStatus {contentPlatformStatusId} not found.");
+
+        if (string.IsNullOrEmpty(cps.PlatformPostId))
+            return Result<EngagementSnapshot>.ValidationFailure(
+                ["Cannot fetch engagement for unpublished post (no PlatformPostId)."]);
+
+        var platform = _platforms.FirstOrDefault(p => p.Type == cps.Platform);
+        if (platform is null)
+            return Result<EngagementSnapshot>.Failure(
+                ErrorCode.InternalError, $"No adapter registered for platform {cps.Platform}.");
+
+        var rateLimitCheck = await _rateLimiter.CanMakeRequestAsync(
+            cps.Platform, "engagement", ct);
+
+        if (!rateLimitCheck.IsSuccess)
+            return Result<EngagementSnapshot>.Failure(rateLimitCheck.ErrorCode,
+                rateLimitCheck.Errors.ToArray());
+
+        if (!rateLimitCheck.Value!.Allowed)
+            return Result<EngagementSnapshot>.Failure(ErrorCode.Conflict,
+                rateLimitCheck.Value.Reason ?? "Rate limited.");
+
+        var engagementResult = await platform.GetEngagementAsync(cps.PlatformPostId, ct);
+        if (!engagementResult.IsSuccess)
+            return Result<EngagementSnapshot>.Failure(engagementResult.ErrorCode,
+                engagementResult.Errors.ToArray());
+
+        var stats = engagementResult.Value!;
+        var snapshot = new EngagementSnapshot
+        {
+            ContentPlatformStatusId = contentPlatformStatusId,
+            Likes = stats.Likes,
+            Comments = stats.Comments,
+            Shares = stats.Shares,
+            Impressions = stats.Impressions,
+            Clicks = stats.Clicks,
+            FetchedAt = DateTimeOffset.UtcNow,
+        };
+
+        _db.EngagementSnapshots.Add(snapshot);
+        await _db.SaveChangesAsync(ct);
+
+        await _rateLimiter.RecordRequestAsync(cps.Platform, "engagement", 0, null, ct);
+
+        _logger.LogInformation(
+            "Fetched engagement for {Platform} post {PostId}: {Likes}L/{Comments}C/{Shares}S",
+            cps.Platform, cps.PlatformPostId, stats.Likes, stats.Comments, stats.Shares);
+
+        return Result<EngagementSnapshot>.Success(snapshot);
+    }
+
+    public async Task<Result<ContentPerformanceReport>> GetPerformanceAsync(
+        Guid contentId, CancellationToken ct)
+    {
+        var publishedStatuses = await _db.ContentPlatformStatuses
+            .Where(s => s.ContentId == contentId && s.Status == PlatformPublishStatus.Published)
+            .ToListAsync(ct);
+
+        var statusIds = publishedStatuses.Select(s => s.Id).ToHashSet();
+
+        var latestByPlatform = new Dictionary<PlatformType, EngagementSnapshot>();
+
+        foreach (var cps in publishedStatuses)
+        {
+            var latestSnapshot = await _db.EngagementSnapshots
+                .Where(e => e.ContentPlatformStatusId == cps.Id)
+                .OrderByDescending(e => e.FetchedAt)
+                .FirstOrDefaultAsync(ct);
+
+            if (latestSnapshot is not null)
+                latestByPlatform[cps.Platform] = latestSnapshot;
+        }
+
+        var totalEngagement = latestByPlatform.Values
+            .Sum(s => s.Likes + s.Comments + s.Shares);
+
+        var completedExecutions = await _db.AgentExecutions
+            .Where(e => e.ContentId == contentId && e.Status == AgentExecutionStatus.Completed)
+            .ToListAsync(ct);
+
+        decimal? llmCost = completedExecutions.Count > 0
+            ? completedExecutions.Sum(e => e.Cost)
+            : null;
+
+        decimal? costPerEngagement = totalEngagement > 0 && llmCost is > 0
+            ? llmCost.Value / totalEngagement
+            : null;
+
+        return Result<ContentPerformanceReport>.Success(new ContentPerformanceReport(
+            contentId, latestByPlatform, totalEngagement, llmCost, costPerEngagement));
+    }
+
+    public async Task<Result<IReadOnlyList<TopPerformingContent>>> GetTopContentAsync(
+        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
+    {
+        var publishedStatuses = await _db.ContentPlatformStatuses
+            .Where(s => s.Status == PlatformPublishStatus.Published
+                        && s.PublishedAt >= from && s.PublishedAt <= to)
+            .ToListAsync(ct);
+
+        if (publishedStatuses.Count == 0)
+            return Result<IReadOnlyList<TopPerformingContent>>.Success(
+                Array.Empty<TopPerformingContent>());
+
+        var statusIds = publishedStatuses.Select(s => s.Id).ToHashSet();
+
+        var allSnapshots = await _db.EngagementSnapshots
+            .Where(e => statusIds.Contains(e.ContentPlatformStatusId))
+            .ToListAsync(ct);
+
+        // Group snapshots by ContentPlatformStatusId, take latest per status
+        var latestPerStatus = allSnapshots
+            .GroupBy(s => s.ContentPlatformStatusId)
+            .Select(g => g.OrderByDescending(s => s.FetchedAt).First())
+            .ToList();
+
+        // Map status ID -> ContentPlatformStatus for platform/content lookup
+        var statusLookup = publishedStatuses.ToDictionary(s => s.Id);
+
+        // Group latest snapshots by ContentId
+        var byContent = latestPerStatus
+            .Select(snap => new { Snapshot = snap, Status = statusLookup[snap.ContentPlatformStatusId] })
+            .GroupBy(x => x.Status.ContentId)
+            .Select(g =>
+            {
+                var totalEngagement = g.Sum(x => x.Snapshot.Likes + x.Snapshot.Comments + x.Snapshot.Shares);
+                var engagementByPlatform = g.ToDictionary(
+                    x => x.Status.Platform,
+                    x => x.Snapshot.Likes + x.Snapshot.Comments + x.Snapshot.Shares);
+
+                return new
+                {
+                    ContentId = g.Key,
+                    TotalEngagement = totalEngagement,
+                    EngagementByPlatform = engagementByPlatform,
+                };
+            })
+            .OrderByDescending(x => x.TotalEngagement)
+            .Take(limit)
+            .ToList();
+
+        // Load titles
+        var contentIds = byContent.Select(x => x.ContentId).ToHashSet();
+        var contentTitles = await _db.Contents
+            .Where(c => contentIds.Contains(c.Id))
+            .ToDictionaryAsync(c => c.Id, c => c.Title ?? "(Untitled)", ct);
+
+        var results = byContent
+            .Select(x => new TopPerformingContent(
+                x.ContentId,
+                contentTitles.GetValueOrDefault(x.ContentId, "(Untitled)"),
+                x.TotalEngagement,
+                x.EngagementByPlatform))
+            .ToList();
+
+        return Result<IReadOnlyList<TopPerformingContent>>.Success(results);
+    }
+
+    public async Task<Result<int>> CleanupSnapshotsAsync(CancellationToken ct)
+    {
+        var now = DateTimeOffset.UtcNow;
+        var dailyCutoff = now.AddDays(-7);
+        var deleteCutoff = now.AddDays(-_options.EngagementRetentionDays);
+
+        // Delete everything older than retention period
+        var oldSnapshots = await _db.EngagementSnapshots
+            .Where(s => s.FetchedAt < deleteCutoff)
+            .ToListAsync(ct);
+
+        // For 7-30 day range, keep only one snapshot per day per status
+        var consolidationSnapshots = await _db.EngagementSnapshots
+            .Where(s => s.FetchedAt >= deleteCutoff && s.FetchedAt < dailyCutoff)
+            .ToListAsync(ct);
+
+        var toRemove = consolidationSnapshots
+            .GroupBy(s => new { s.ContentPlatformStatusId, Day = s.FetchedAt.Date })
+            .SelectMany(g => g.OrderByDescending(s => s.FetchedAt).Skip(1))
+            .ToList();
+
+        var totalRemoved = oldSnapshots.Count + toRemove.Count;
+
+        if (totalRemoved > 0)
+        {
+            _db.EngagementSnapshots.RemoveRange(oldSnapshots);
+            _db.EngagementSnapshots.RemoveRange(toRemove);
+            await _db.SaveChangesAsync(ct);
+
+            _logger.LogInformation(
+                "Cleaned up {Count} engagement snapshots ({Old} expired, {Consolidated} consolidated)",
+                totalRemoved, oldSnapshots.Count, toRemove.Count);
+        }
+
+        return Result<int>.Success(totalRemoved);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/EngagementAggregatorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/EngagementAggregatorTests.cs
new file mode 100644
index 0000000..19cf770
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/EngagementAggregatorTests.cs
@@ -0,0 +1,394 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;
+
+public class EngagementAggregatorTests
+{
+    private readonly Mock<IApplicationDbContext> _db = new();
+    private readonly Mock<ISocialPlatform> _twitterPlatform = new();
+    private readonly Mock<ISocialPlatform> _linkedInPlatform = new();
+    private readonly Mock<IRateLimiter> _rateLimiter = new();
+    private readonly Mock<ILogger<EngagementAggregator>> _logger = new();
+    private readonly ContentEngineOptions _options = new();
+
+    public EngagementAggregatorTests()
+    {
+        _twitterPlatform.Setup(p => p.Type).Returns(PlatformType.TwitterX);
+        _linkedInPlatform.Setup(p => p.Type).Returns(PlatformType.LinkedIn);
+    }
+
+    private EngagementAggregator CreateSut(IEnumerable<ISocialPlatform>? platforms = null) => new(
+        _db.Object,
+        platforms ?? [_twitterPlatform.Object, _linkedInPlatform.Object],
+        _rateLimiter.Object,
+        Options.Create(_options),
+        _logger.Object);
+
+    private static ContentPlatformStatus CreateStatus(
+        Guid contentId,
+        PlatformType platform,
+        string? platformPostId = "post-123",
+        PlatformPublishStatus status = PlatformPublishStatus.Published)
+    {
+        var cps = new ContentPlatformStatus
+        {
+            ContentId = contentId,
+            Platform = platform,
+            Status = status,
+            PlatformPostId = platformPostId,
+            PublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
+        };
+        // Set Id via reflection since it's from EntityBase
+        typeof(ContentPlatformStatus).BaseType!.BaseType!
+            .GetProperty("Id")!.SetValue(cps, Guid.NewGuid());
+        return cps;
+    }
+
+    private static EngagementSnapshot CreateSnapshot(
+        Guid contentPlatformStatusId,
+        int likes = 10,
+        int comments = 5,
+        int shares = 3,
+        DateTimeOffset? fetchedAt = null)
+    {
+        var snapshot = new EngagementSnapshot
+        {
+            ContentPlatformStatusId = contentPlatformStatusId,
+            Likes = likes,
+            Comments = comments,
+            Shares = shares,
+            Impressions = 100,
+            Clicks = 50,
+            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
+        };
+        typeof(EngagementSnapshot).BaseType!.BaseType!
+            .GetProperty("Id")!.SetValue(snapshot, Guid.NewGuid());
+        return snapshot;
+    }
+
+    private void SetupDbSets(
+        ContentPlatformStatus[]? statuses = null,
+        EngagementSnapshot[]? snapshots = null,
+        AgentExecution[]? executions = null,
+        Content[]? contents = null)
+    {
+        var statusMock = (statuses ?? []).AsQueryable().BuildMockDbSet();
+        _db.Setup(d => d.ContentPlatformStatuses).Returns(statusMock.Object);
+
+        var snapshotMock = (snapshots ?? []).AsQueryable().BuildMockDbSet();
+        _db.Setup(d => d.EngagementSnapshots).Returns(snapshotMock.Object);
+
+        var executionMock = (executions ?? []).AsQueryable().BuildMockDbSet();
+        _db.Setup(d => d.AgentExecutions).Returns(executionMock.Object);
+
+        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
+        _db.Setup(d => d.Contents).Returns(contentMock.Object);
+    }
+
+    // ── FetchLatestAsync ──
+
+    [Fact]
+    public async Task FetchLatestAsync_ValidContentPlatformStatus_CallsGetEngagementAndSavesSnapshot()
+    {
+        // Arrange
+        var contentId = Guid.NewGuid();
+        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tweet-456");
+
+        SetupDbSets(statuses: [cps]);
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+
+        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(true));
+
+        var stats = new EngagementStats(10, 5, 3, 100, 50, new Dictionary<string, int>());
+        _twitterPlatform.Setup(p => p.GetEngagementAsync("tweet-456", It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(stats));
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        Assert.Equal(10, result.Value!.Likes);
+        Assert.Equal(5, result.Value.Comments);
+        Assert.Equal(3, result.Value.Shares);
+        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task FetchLatestAsync_NoPlatformPostId_ReturnsValidationError()
+    {
+        // Arrange
+        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, platformPostId: null);
+        SetupDbSets(statuses: [cps]);
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);
+
+        // Assert
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task FetchLatestAsync_RateLimited_ReturnsError()
+    {
+        // Arrange
+        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, "tweet-789");
+        SetupDbSets(statuses: [cps]);
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(false, DateTimeOffset.UtcNow.AddMinutes(5), "Rate limit exceeded")));
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);
+
+        // Assert
+        Assert.False(result.IsSuccess);
+        _twitterPlatform.Verify(p => p.GetEngagementAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task FetchLatestAsync_PlatformApiError_ReturnsErrorResult()
+    {
+        // Arrange
+        var cps = CreateStatus(Guid.NewGuid(), PlatformType.TwitterX, "tweet-fail");
+        SetupDbSets(statuses: [cps]);
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+
+        _twitterPlatform.Setup(p => p.GetEngagementAsync("tweet-fail", It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<EngagementStats>(ErrorCode.InternalError, "API error"));
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.FetchLatestAsync(cps.Id, CancellationToken.None);
+
+        // Assert
+        Assert.False(result.IsSuccess);
+        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task FetchLatestAsync_NotFound_ReturnsNotFoundError()
+    {
+        // Arrange
+        SetupDbSets(statuses: []);
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.FetchLatestAsync(Guid.NewGuid(), CancellationToken.None);
+
+        // Assert
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // ── GetPerformanceAsync ──
+
+    [Fact]
+    public async Task GetPerformanceAsync_MultiPlatformContent_AggregatesCorrectly()
+    {
+        // Arrange
+        var contentId = Guid.NewGuid();
+        var twitterCps = CreateStatus(contentId, PlatformType.TwitterX, "tw-1");
+        var linkedInCps = CreateStatus(contentId, PlatformType.LinkedIn, "li-1");
+
+        var twitterSnapshot = CreateSnapshot(twitterCps.Id, likes: 10, comments: 5, shares: 3);
+        var linkedInSnapshot = CreateSnapshot(linkedInCps.Id, likes: 20, comments: 10, shares: 7);
+
+        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard, contentId);
+        execution.MarkRunning();
+        execution.RecordUsage("claude-3", 1000, 500, 0, 0, 0.05m);
+        execution.Complete();
+
+        SetupDbSets(
+            statuses: [twitterCps, linkedInCps],
+            snapshots: [twitterSnapshot, linkedInSnapshot],
+            executions: [execution]);
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        var report = result.Value!;
+        Assert.Equal(contentId, report.ContentId);
+        Assert.Equal(2, report.LatestByPlatform.Count);
+        // Total = (10+5+3) + (20+10+7) = 55
+        Assert.Equal(55, report.TotalEngagement);
+        Assert.Equal(0.05m, report.LlmCost);
+        Assert.NotNull(report.CostPerEngagement);
+    }
+
+    [Fact]
+    public async Task GetPerformanceAsync_ZeroEngagement_CostPerEngagementIsNull()
+    {
+        // Arrange
+        var contentId = Guid.NewGuid();
+        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tw-zero");
+        var snapshot = CreateSnapshot(cps.Id, likes: 0, comments: 0, shares: 0);
+
+        SetupDbSets(
+            statuses: [cps],
+            snapshots: [snapshot],
+            executions: []);
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        Assert.Equal(0, result.Value!.TotalEngagement);
+        Assert.Null(result.Value.CostPerEngagement);
+    }
+
+    [Fact]
+    public async Task GetPerformanceAsync_NoAgentExecutions_LlmCostIsNull()
+    {
+        // Arrange
+        var contentId = Guid.NewGuid();
+        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tw-nocost");
+        var snapshot = CreateSnapshot(cps.Id, likes: 10, comments: 5, shares: 3);
+
+        SetupDbSets(
+            statuses: [cps],
+            snapshots: [snapshot],
+            executions: []);
+
+        var sut = CreateSut();
+
+        // Act
+        var result = await sut.GetPerformanceAsync(contentId, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        Assert.Null(result.Value!.LlmCost);
+        Assert.Null(result.Value.CostPerEngagement);
+    }
+
+    // ── GetTopContentAsync ──
+
+    [Fact]
+    public async Task GetTopContentAsync_ReturnsOrderedByTotalEngagement()
+    {
+        // Arrange
+        var content1Id = Guid.NewGuid();
+        var content2Id = Guid.NewGuid();
+        var content3Id = Guid.NewGuid();
+
+        var content1 = Content.Create(ContentType.BlogPost, "body1", "Low Performer");
+        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content1, content1Id);
+
+        var content2 = Content.Create(ContentType.BlogPost, "body2", "Top Performer");
+        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content2, content2Id);
+
+        var content3 = Content.Create(ContentType.BlogPost, "body3", "Mid Performer");
+        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content3, content3Id);
+
+        var cps1 = CreateStatus(content1Id, PlatformType.TwitterX, "tw-low");
+        var cps2 = CreateStatus(content2Id, PlatformType.TwitterX, "tw-top");
+        var cps3 = CreateStatus(content3Id, PlatformType.TwitterX, "tw-mid");
+
+        var snap1 = CreateSnapshot(cps1.Id, likes: 2, comments: 1, shares: 0); // Total: 3
+        var snap2 = CreateSnapshot(cps2.Id, likes: 50, comments: 25, shares: 15); // Total: 90
+        var snap3 = CreateSnapshot(cps3.Id, likes: 10, comments: 5, shares: 3); // Total: 18
+
+        SetupDbSets(
+            statuses: [cps1, cps2, cps3],
+            snapshots: [snap1, snap2, snap3],
+            contents: [content1, content2, content3]);
+
+        var sut = CreateSut();
+
+        // Act
+        var from = DateTimeOffset.UtcNow.AddDays(-30);
+        var to = DateTimeOffset.UtcNow;
+        var result = await sut.GetTopContentAsync(from, to, limit: 2, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Count);
+        Assert.Equal("Top Performer", result.Value[0].Title);
+        Assert.Equal(90, result.Value[0].TotalEngagement);
+        Assert.Equal("Mid Performer", result.Value[1].Title);
+        Assert.Equal(18, result.Value[1].TotalEngagement);
+    }
+
+    [Fact]
+    public async Task GetTopContentAsync_UsesLatestSnapshotPerPlatform()
+    {
+        // Arrange
+        var contentId = Guid.NewGuid();
+        var content = Content.Create(ContentType.BlogPost, "body", "Test Content");
+        typeof(Content).BaseType!.BaseType!.GetProperty("Id")!.SetValue(content, contentId);
+
+        var cps = CreateStatus(contentId, PlatformType.TwitterX, "tw-multi");
+
+        var oldSnapshot = CreateSnapshot(cps.Id, likes: 5, comments: 2, shares: 1,
+            fetchedAt: DateTimeOffset.UtcNow.AddHours(-6));
+        var newSnapshot = CreateSnapshot(cps.Id, likes: 50, comments: 20, shares: 10,
+            fetchedAt: DateTimeOffset.UtcNow);
+
+        SetupDbSets(
+            statuses: [cps],
+            snapshots: [oldSnapshot, newSnapshot],
+            contents: [content]);
+
+        var sut = CreateSut();
+
+        // Act
+        var from = DateTimeOffset.UtcNow.AddDays(-30);
+        var to = DateTimeOffset.UtcNow;
+        var result = await sut.GetTopContentAsync(from, to, limit: 10, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+        // Should use latest snapshot: 50+20+10 = 80, not old: 5+2+1 = 8
+        Assert.Equal(80, result.Value![0].TotalEngagement);
+    }
+
+    [Fact]
+    public async Task GetTopContentAsync_EmptyRange_ReturnsEmptyList()
+    {
+        // Arrange
+        SetupDbSets(statuses: [], snapshots: [], contents: []);
+
+        var sut = CreateSut();
+
+        // Act
+        var from = DateTimeOffset.UtcNow.AddDays(-1);
+        var to = DateTimeOffset.UtcNow;
+        var result = await sut.GetTopContentAsync(from, to, limit: 10, CancellationToken.None);
+
+        // Assert
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!);
+    }
+}
